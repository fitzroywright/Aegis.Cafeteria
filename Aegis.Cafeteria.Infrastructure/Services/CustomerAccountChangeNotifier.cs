namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Accounts;

using Microsoft.Extensions.Logging;

public sealed class CustomerAccountChangeNotifier : ICustomerAccountChangeNotifier
{
    private readonly object synchronizationLock = new();
    private readonly List<Func<int, Task>> handlers = [];
    private readonly ILogger<CustomerAccountChangeNotifier> logger;

    public CustomerAccountChangeNotifier
    (
        ILogger<CustomerAccountChangeNotifier> logger
    )
    {
        this.logger = logger;
    }

    public IDisposable Subscribe
    (
        Func<int, Task> handler
    )
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (this.synchronizationLock)
        {
            this.handlers.Add(handler);
        }

        return new Subscription
        (
              this
            , handler
        );
    }

    public async Task NotifyAsync
    (
        int customerId
    )
    {
        Func<int, Task>[] subscribers;

        lock (this.synchronizationLock)
        {
            subscribers = [.. this.handlers];
        }

        foreach (Func<int, Task> subscriber in subscribers)
        {
            try
            {
                await subscriber(customerId);
            }
            catch (Exception exception)
            {
                this.logger.LogWarning
                (
                      exception
                    , "A customer-account change subscriber failed for customer {CustomerId}"
                    , customerId
                );
            }
        }
    }

    private void Unsubscribe
    (
        Func<int, Task> handler
    )
    {
        lock (this.synchronizationLock)
        {
            this.handlers.Remove(handler);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly CustomerAccountChangeNotifier owner;
        private readonly Func<int, Task> handler;
        private bool disposed;

        public Subscription
        (
              CustomerAccountChangeNotifier owner
            , Func<int, Task> handler
        )
        {
            this.owner = owner;
            this.handler = handler;
        }

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;
            this.owner.Unsubscribe(this.handler);
        }
    }
}
