namespace Aegis.Cafeteria.Infrastructure.Services;

public sealed class ManagementEventWakeSignal
{
    private readonly SemaphoreSlim signal = new
    (
          initialCount: 0
        , maxCount: 1
    );

    public void Pulse()
    {
        try
        {
            this.signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // One pending wake-up is enough. Durable polling remains the safety net.
        }
    }

    public async Task WaitAsync
    (
          TimeSpan timeout
        , CancellationToken cancellationToken
    )
    {
        await this.signal.WaitAsync
        (
              timeout
            , cancellationToken
        );
    }
}
