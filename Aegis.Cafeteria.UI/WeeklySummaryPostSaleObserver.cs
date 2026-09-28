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
                "Could not load weekly email preference for customer {CustomerId}; deferring the prompt until Services is available",
                account.Customer.Id);
            return;
        }

        WeeklySummaryPromptDecision decision = WeeklySummaryPromptPolicy.Evaluate
        (
              account
            , preference.PreferenceAsked && !preference.NeedsEmailReview
            , preference.EmailAddress ?? preference.SuggestedEmailAddress
            , preference.SuggestedEmailSource is not null
        );

        if (!decision.ShouldPrompt)
        {
            return;
        }

        Form? owner = System.Windows.Forms.Application.OpenForms.Cast<Form>().FirstOrDefault(form => form is MainForm);
        if (owner is null || owner.IsDisposed || !owner.IsHandleCreated)
        {
            return;
        }

        TaskCompletionSource<(DialogResult Result, string Email)> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.BeginInvoke(new Action(() =>
        {
            try
            {
                using WeeklySummaryOptInDialog dialog = new(account.Customer.Name, decision.SuggestedEmail, preference.NeedsEmailReview);
                DialogResult choice = dialog.ShowDialog(owner);
                completion.TrySetResult((choice, dialog.EmailAddress));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }));
        (DialogResult result, string email) = await completion.Task.WaitAsync(cancellationToken);
        if (result != DialogResult.Yes && result != DialogResult.No)
        {
            return;
        }

        try
        {
            await this.servicesClient.SetNotificationPreferenceAsync(
                account.Customer.Id,
                result == DialogResult.Yes,
                result == DialogResult.Yes ? email : null,
                cancellationToken,
                NotificationEmailChoice.AcknowledgedSource(preference, result == DialogResult.Yes, email),
                preference.SuggestedEmailAddress);
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

