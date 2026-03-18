using System.Diagnostics;

namespace HaPcRemote.Service.Services;

public class CliRunner : ICliRunner
{
    public async Task<string> RunAsync(string exePath, IEnumerable<string> arguments, int timeoutMs = 10000)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"CLI tool not found: {exePath}", exePath);

        var effectiveTimeout = TimeSpan.FromMilliseconds(timeoutMs);

        using var process = new Process();
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        process.StartInfo = startInfo;

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(effectiveTimeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Process '{exePath}' timed out after {effectiveTimeout.TotalSeconds}s.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{exePath}' exited with code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }
}
