namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class WeeklySummaryPostSaleObserver : IPostSaleObserver
{
    private readonly CafeteriaServicesClient servicesClient;
    private readonly IHostEnvironment environment;
    private readonly ILogger<WeeklySummaryPostSaleObserver> logger;

    public WeeklySummaryPostSaleObserver(
        CafeteriaServicesClient servicesClient,
        IHostEnvironment environment,
        ILogger<WeeklySummaryPostSaleObserver> logger)
    {
        this.servicesClient = servicesClient;
        this.environment = environment;
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
            preference = await this.servicesClient.GetNotificationPreferenceAsync(
                account.Customer.Id,
                cancellationToken);
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(
                exception,
                "Could not load weekly email preference for customer {CustomerId}; using the local employee cache",
                account.Customer.Id);
            preference = new CustomerNotificationPreferenceSnapshot(account.Customer.Id, false, false, null);
        }

        // Production asks only once automatically. Development deliberately re-prompts so the
        // cashier workflow can be tested repeatedly without editing the Services SQLite database.
        if (preference.PreferenceAsked && !this.environment.IsDevelopment())
        {
            return;
        }

        string suggestedEmail = !string.IsNullOrWhiteSpace(preference.EmailAddress)
            ? preference.EmailAddress
            : account.Customer.EmailAddress;

        using WeeklySummaryOptInDialog dialog = new(account.Customer.Name, suggestedEmail);
        IWin32Window? owner = System.Windows.Forms.Application.OpenForms
            .Cast<Form>()
            .FirstOrDefault(form => form is MainForm && form.Visible);
        DialogResult result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);

        try
        {
            await this.servicesClient.SetNotificationPreferenceAsync(
                account.Customer.Id,
                result == DialogResult.Yes,
                result == DialogResult.Yes ? dialog.EmailAddress : null,
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
