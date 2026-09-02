namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

public sealed class WeeklySummaryPostSaleObserver : IPostSaleObserver
{
    private readonly CafeteriaServicesClient servicesClient;
    private readonly ILogger<WeeklySummaryPostSaleObserver> logger;

    public WeeklySummaryPostSaleObserver(
        CafeteriaServicesClient servicesClient,
        ILogger<WeeklySummaryPostSaleObserver> logger)
    {
        this.servicesClient = servicesClient;
        this.logger = logger;
    }

    public async Task OnSaleCompletedAsync(
        CustomerAccountView account,
        Order order,
        CancellationToken cancellationToken = default)
    {
        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit)
        {
            return;
        }

        try
        {
            CustomerNotificationPreferenceSnapshot preference = await this.servicesClient.GetNotificationPreferenceAsync(account.Customer.Id, cancellationToken);
            if (preference.PreferenceAsked)
            {
                return;
            }

            (bool enabled, string? emailAddress) result = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                WeeklySummaryPreferenceWindow dialog = new(account.Customer.Name, preference.EmailAddress);
                Window? owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner is null)
                {
                    return (false, (string?)null);
                }

                bool enabled = await dialog.ShowDialog<bool>(owner);
                return (enabled, enabled ? dialog.EmailAddress : null);
            });

            await this.servicesClient.SetNotificationPreferenceAsync(
                account.Customer.Id,
                result.enabled,
                result.emailAddress,
                cancellationToken);
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(exception, "Weekly email preference prompt failed for customer {CustomerId} after sale {OrderId}", account.Customer.Id, order.Id);
        }
    }
}
