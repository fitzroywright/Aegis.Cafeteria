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

        CustomerNotificationPreferenceSnapshot preference;
        try
        {
            preference = await this.servicesClient.GetNotificationPreferenceAsync(account.Customer.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(
                exception,
                "Could not load weekly email preference for customer {CustomerId}; showing prompt without a saved/AD suggestion",
                account.Customer.Id);
            preference = new CustomerNotificationPreferenceSnapshot(account.Customer.Id, false, false, null);
        }

        if (preference.PreferenceAsked)
        {
            return;
        }

        WeeklySummaryPreferenceResult? result = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window? owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner is null)
            {
                return null;
            }

            WeeklySummaryPreferenceWindow dialog = new(account.Customer.Name, preference.EmailAddress);
            return await dialog.ShowDialog<WeeklySummaryPreferenceResult?>(owner);
        });

        if (result is null)
        {
            this.logger.LogWarning(
                "Weekly email preference prompt could not find the Linux main window for customer {CustomerId}",
                account.Customer.Id);
            return;
        }

        try
        {
            await this.servicesClient.SetNotificationPreferenceAsync(
                account.Customer.Id,
                result.Enabled,
                result.EmailAddress,
                cancellationToken);
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(
                exception,
                "Could not save weekly email preference for customer {CustomerId} after sale {OrderId}; sale remains committed",
                account.Customer.Id,
                order.Id);
        }
    }
}
