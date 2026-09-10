namespace Aegis.Cafeteria.Infrastructure.Secrets;

using System.Diagnostics;

internal sealed class BwsCliExecutor : IBwsCliExecutor
{
    public async Task<BwsCommandResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string accessToken,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        // Keep the access token out of the command line/process list.
        psi.Environment["BWS_ACCESS_TOKEN"] = accessToken;
        psi.Environment["NO_COLOR"] = "1";

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Bitwarden Secrets Manager CLI could not be started.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Unable to start Bitwarden Secrets Manager CLI '{executable}'. Ensure bws is installed and on PATH.", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Bitwarden Secrets Manager CLI did not respond within {timeout.TotalSeconds:0} seconds.");
        }

        return new BwsCommandResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}
