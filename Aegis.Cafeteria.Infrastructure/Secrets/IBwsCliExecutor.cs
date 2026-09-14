namespace Aegis.Cafeteria.Infrastructure.Secrets;

internal interface IBwsCliExecutor
{
    Task<BwsCommandResult> ExecuteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string accessToken,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed record BwsCommandResult(int ExitCode, string StandardOutput, string StandardError);
