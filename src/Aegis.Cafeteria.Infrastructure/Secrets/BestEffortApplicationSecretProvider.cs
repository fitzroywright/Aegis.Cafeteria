namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;

/// <summary>
/// Makes remote secret resolution non-fatal for the local-first POS. Missing or slow remote secrets
/// disable the online capability that needs them; they do not prevent the local terminal from starting.
/// </summary>
public sealed class BestEffortApplicationSecretProvider
(
      IApplicationSecretProvider inner
    , TimeSpan? maximumWait = null
) : IApplicationSecretProvider
{
    private readonly TimeSpan timeout = maximumWait ?? TimeSpan.FromSeconds(3);

    public async Task<string?> GetSecretAsync
    (
          string logicalName
        , CancellationToken cancellationToken = default
    )
    {
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await inner.GetSecretAsync(logicalName, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
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
