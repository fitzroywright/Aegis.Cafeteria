namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Services;
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
            CustomerNotificationPreferenceSnapshot preference = await this.servicesClient.GetNotificationPreferenceAsync(
                account.Customer.Id,
                cancellationToken);
            if (preference.PreferenceAsked)
            {
                return;
            }

            using WeeklySummaryOptInDialog dialog = new(account.Customer.Name, preference.EmailAddress);
            DialogResult result = dialog.ShowDialog();
            await this.servicesClient.SetNotificationPreferenceAsync(
                account.Customer.Id,
                result == DialogResult.Yes,
                result == DialogResult.Yes ? dialog.EmailAddress : null,
                cancellationToken);
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(exception, "Weekly email preference prompt failed for customer {CustomerId} after sale {OrderId}", account.Customer.Id, order.Id);
        }
    }
}
