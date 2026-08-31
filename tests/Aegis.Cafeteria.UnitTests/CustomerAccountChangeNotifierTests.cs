namespace Aegis.Cafeteria.UnitTests;

using Aegis.Cafeteria.Infrastructure.Services;

using Microsoft.Extensions.Logging.Abstractions;

public sealed class CustomerAccountChangeNotifierTests
{
    [Fact]
    public async Task NotifyAsync_NotifiesSubscribedCustomerHandler()
    {
        CustomerAccountChangeNotifier notifier = new
        (
            NullLogger<CustomerAccountChangeNotifier>.Instance
        );

        int? notifiedCustomerId = null;

        using IDisposable subscription = notifier.Subscribe
        (
            customerId =>
            {
                notifiedCustomerId = customerId;
                return Task.CompletedTask;
            }
        );

        await notifier.NotifyAsync(123);

        Assert.Equal
        (
              123
            , notifiedCustomerId
        );
    }

    [Fact]
    public async Task DisposedSubscription_IsNotNotified()
    {
        CustomerAccountChangeNotifier notifier = new
        (
            NullLogger<CustomerAccountChangeNotifier>.Instance
        );

        int notificationCount = 0;

        IDisposable subscription = notifier.Subscribe
        (
            customerId =>
            {
                notificationCount++;
                return Task.CompletedTask;
            }
        );

        subscription.Dispose();

        await notifier.NotifyAsync(123);

        Assert.Equal
        (
              0
            , notificationCount
        );
    }
}
