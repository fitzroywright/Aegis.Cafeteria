namespace Aegis.Cafeteria.UnitTests;

using Aegis.Cafeteria.Infrastructure.Services;

public sealed class ManagementEventWakeSignalTests
{
    [Fact]
    public async Task Pulse_ReleasesWaitingSynchronizationLoop()
    {
        ManagementEventWakeSignal wakeSignal = new();

        wakeSignal.Pulse();

        await wakeSignal.WaitAsync
        (
              TimeSpan.FromSeconds(1)
            , CancellationToken.None
        );
    }

    [Fact]
    public void RepeatedPulse_DoesNotOverflowPendingWakeup()
    {
        ManagementEventWakeSignal wakeSignal = new();

        wakeSignal.Pulse();
        wakeSignal.Pulse();
        wakeSignal.Pulse();
    }
}
