using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YouTubeMP3.Windows.Models;

namespace YouTubeMP3.Windows.Services;

public sealed class YouTubeService
{
    private readonly ToolManager _tools = new();

    public async Task<IReadOnlyList<VideoItem>> SearchAsync(
        string query, int count, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<VideoItem>();
        }

        var ytDlp = await _tools.GetYtDlpAsync(cancellationToken);
        var json = await ProcessRunner.RunAsync(ytDlp, new[]
        {
            "--no-config", "--no-warnings", "--flat-playlist", "--dump-single-json",
            $"ytsearch{count}:{query.Trim()}"
        }, cancellationToken: cancellationToken);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<VideoItem>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<VideoItem>();
        foreach (var entry in entries.EnumerateArray())
        {
            var id = GetString(entry, "id");
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$") || !seen.Add(id))
            {
                continue;
            }

            var duration = GetNumber(entry, "duration");
            var seconds = duration is > 0 ? (int)duration.Value : 0;
            items.Add(new VideoItem
            {
                Id = id,
                Title = GetString(entry, "title"),
                Channel = GetString(entry, "channel") is { Length: > 0 } channel
                    ? channel : GetString(entry, "uploader"),
                Duration = seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss") : "",
                DurationSeconds = duration
            });
        }

        return items;
    }

    public async Task<string> DownloadAndConvertAsync(
        VideoItem video,
        int bitrate,
        IProgress<double> downloadProgress,
        IProgress<double> conversionProgress,
        CancellationToken cancellationToken = default)
    {
        if (!new[] { 128, 192, 256, 320 }.Contains(bitrate))
        {
            throw new ArgumentOutOfRangeException(nameof(bitrate));
        }

        var ytDlp = await _tools.GetYtDlpAsync(cancellationToken);
        var ffmpeg = await _tools.GetFfmpegAsync(cancellationToken);
        var work = Path.Combine(Path.GetTempPath(), "YouTubeMP3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            var template = Path.Combine(work, "media.%(ext)s");
            await ProcessRunner.RunAsync(ytDlp, new[]
            {
                "--no-config", "--no-playlist", "--no-warnings", "--newline",
                "--no-part", "--progress", "--progress-template",
                "download:DL_PROGRESS:%(progress._percent_str)s",
                "-f", "bestaudio[ext=m4a]/bestaudio[acodec^=mp4a]/bestaudio/best",
                "-o", template,
                video.WatchUrl
            }, line =>
            {
                var match = Regex.Match(line, @"DL_PROGRESS:\s*([\d.]+)%");
                if (match.Success && double.TryParse(match.Groups[1].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    downloadProgress.Report(Math.Clamp(percent / 100.0, 0, 1));
                }
            }, cancellationToken);

            var media = Directory.EnumerateFiles(work, "media.*")
                .FirstOrDefault(file => !file.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase));
            if (media is null)
            {
                throw new FileNotFoundException(
                    "yt-dlp 실행은 완료되었으나 다운로드된 오디오 파일이 없습니다. " +
                    "YouTube 다운로드 제한, 접근 권한 또는 추출 형식을 확인하세요.");
            }

            downloadProgress.Report(1);
            var outputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "YouTubeMP3");
            Directory.CreateDirectory(outputDirectory);
            var title = SafeName(video.Title);
            var destination = UniquePath(outputDirectory, title);
            var pending = Path.Combine(work, "encoded.mp3");

            await ProcessRunner.RunAsync(ffmpeg, new[]
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-i", media,
                "-vn", "-map", "0:a:0", "-ar", "44100", "-ac", "2",
                "-c:a", "libmp3lame", "-b:a", $"{bitrate}k",
                "-q:a", "5", "-progress", "pipe:1", "-nostats", pending
            }, line =>
            {
                if (video.DurationSeconds is not > 0)
                {
                    return;
                }

                if (line.StartsWith("out_time=", StringComparison.Ordinal) &&
                    TimeSpan.TryParse(line["out_time=".Length..], CultureInfo.InvariantCulture, out var elapsed))
                {
                    conversionProgress.Report(Math.Clamp(elapsed.TotalSeconds / video.DurationSeconds.Value, 0, 0.99));
                }
            }, cancellationToken);

            File.Move(pending, destination);
            conversionProgress.Report(1);
            return destination;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static string GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    }

    private static double? GetNumber(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number) ? number : null;
    }

    private static string SafeName(string title)
    {
        var name = string.Join("_", title.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? "YouTube Audio" : name.Length > 150 ? name[..150] : name;
    }

    private static string UniquePath(string directory, string title)
    {
        var basePath = Path.Combine(directory, title + ".mp3");
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        for (var index = 2; index < 100000; index++)
        {
            var path = Path.Combine(directory, $"{title} ({index}).mp3");
            if (!File.Exists(path))
            {
                return path;
            }
        }

        throw new IOException("중복 파일명이 너무 많습니다.");
    }
}
