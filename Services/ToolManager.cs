using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace YouTubeMP3.Windows.Services;

public sealed class ToolManager
{
    private const string Version = "2026.10.26";
    private const string ReleaseBase = "https://github.com/yt-dlp/yt-dlp/releases/download/" + Version + "/";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _toolDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YouTubeMP3", "Tools");

    public async Task<string> GetYtDlpAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_toolDirectory);
        var path = Path.Combine(_toolDirectory, $"yt-dlp-{Version}.exe");
        var manifest = await Client.GetStringAsync(ReleaseBase + "SHA2-256SUMS", cancellationToken);
        var expected = manifest.Split('\n')
            .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2 && parts[^1].TrimStart('*') == "yt-dlp.exe")
            .Select(parts => parts[0].ToLowerInvariant())
            .FirstOrDefault();

        if (expected is null || expected.Length != 64)
        {
            throw new InvalidOperationException("공식 yt-dlp 체크섬 목록에서 yt-dlp.exe를 찾지 못했습니다.");
        }

        if (File.Exists(path) && await HashMatchesAsync(path, expected, cancellationToken))
        {
            return path;
        }

        var pending = path + ".download";
        try
        {
            using var response = await Client.GetAsync(ReleaseBase + "yt-dlp.exe", cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(pending))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!await HashMatchesAsync(pending, expected, cancellationToken))
            {
                throw new InvalidOperationException("yt-dlp SHA-256 무결성 검증이 실패했습니다.");
            }

            File.Move(pending, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private const string FfmpegBase = "https://www.gyan.dev/ffmpeg/builds/";
    private const string FfmpegArchive = "ffmpeg-release-essentials.zip";

    // Install only the executable needed for libmp3lame encoding. The full ZIP is not
    // shipped with this source project. Verify the publisher's SHA-256 first.
    public async Task<string> GetFfmpegAsync(CancellationToken cancellationToken = default)
    {
        var existing = FindFfmpegPath();
        if (existing is not null)
        {
            return existing;
        }

        Directory.CreateDirectory(_toolDirectory);
        var destination = Path.Combine(_toolDirectory, "ffmpeg.exe");
        var pendingArchive = Path.Combine(_toolDirectory, "ffmpeg-" + Guid.NewGuid().ToString("N") + ".zip");
        var pendingBinary = Path.Combine(_toolDirectory, "ffmpeg-" + Guid.NewGuid().ToString("N") + ".exe");

        try
        {
            var checksumText = await Client.GetStringAsync(
                FfmpegBase + FfmpegArchive + ".sha256", cancellationToken);
            var checksum = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (checksum is null || checksum.Length != 64 ||
                !checksum.All(Uri.IsHexDigit))
            {
                throw new InvalidOperationException("FFmpeg SHA-256 체크섬을 읽을 수 없습니다.");
            }

            using (var response = await Client.GetAsync(
                FfmpegBase + FfmpegArchive, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var output = new FileStream(pendingArchive, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            if (!await HashMatchesAsync(pendingArchive, checksum, cancellationToken))
            {
                throw new InvalidOperationException("FFmpeg 파일 검증(SHA-256)에 실패했습니다.");
            }

            using (var archive = ZipFile.OpenRead(pendingArchive))
            {
                var executable = archive.Entries.FirstOrDefault(entry =>
                    entry.FullName.Replace('\\', '/').EndsWith("/bin/ffmpeg.exe",
                        StringComparison.OrdinalIgnoreCase));

                if (executable is null)
                {
                    throw new FileNotFoundException("FFmpeg 배포 압축파일에 ffmpeg.exe가 없습니다.");
                }

                await using var input = executable.Open();
                await using var output = new FileStream(pendingBinary, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            if (new FileInfo(pendingBinary).Length == 0)
            {
                throw new InvalidOperationException("FFmpeg 실행 파일이 비어 있습니다.");
            }

            File.Move(pendingBinary, destination, overwrite: true);
            return destination;
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                "FFmpeg 자동 다운로드에 실패했습니다. 인터넷 연결을 확인하거나 " +
                "실행 폴더의 Tools에 ffmpeg.exe를 넣으세요. " + exception.Message, exception);
        }
        finally
        {
            if (File.Exists(pendingArchive))
            {
                File.Delete(pendingArchive);
            }

            if (File.Exists(pendingBinary))
            {
                File.Delete(pendingBinary);
            }
        }
    }

    private string? FindFfmpegPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(_toolDirectory, "ffmpeg.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Ignore malformed PATH entries; continue looking.
            }
        }

        return null;
    }
}
