namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;

/// <summary>
/// Makes remote secret resolution non-fatal for the local-first POS. Missing remote secrets
/// disable the online capability that needs them; they do not prevent the local terminal from starting.
/// </summary>
public sealed class BestEffortApplicationSecretProvider
(
    IApplicationSecretProvider inner
) : IApplicationSecretProvider
{
    public async Task<string?> GetSecretAsync
    (
          string logicalName
        , CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await inner.GetSecretAsync(logicalName, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class NullApplicationSecretProvider : IApplicationSecretProvider
{
    public Task<string?> GetSecretAsync
    (
          string logicalName
        , CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<string?>(null);
    }
}
