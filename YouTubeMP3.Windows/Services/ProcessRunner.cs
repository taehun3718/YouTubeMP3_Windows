using System.IO;
using System.Diagnostics;
using System.Text;

namespace YouTubeMP3.Windows.Services;

public static class ProcessRunner
{
    public static async Task<string> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onLine = null,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        var log = new StringBuilder();
        var logLock = new object();

        void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            onLine?.Invoke(line);
            lock (logLock)
            {
                log.AppendLine(line);
                if (log.Length > 100000)
                {
                    log.Remove(0, log.Length - 50000);
                }
            }
        }

        if (!process.Start())
        {
            throw new InvalidOperationException($"프로세스를 시작할 수 없습니다: {fileName}");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        async Task ReadLinesAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                Append(line);
            }
        }

        await Task.WhenAll(
            ReadLinesAsync(process.StandardOutput),
            ReadLinesAsync(process.StandardError),
            process.WaitForExitAsync());

        cancellationToken.ThrowIfCancellationRequested();
        string output;
        lock (logLock)
        {
            output = log.ToString();
        }

        if (process.ExitCode != 0)
        {
            var detail = output.Length > 3000 ? output[^3000..] : output;
            throw new InvalidOperationException($"프로세스 종료 코드 {process.ExitCode}: {detail}");
        }

        return output;
    }
}
