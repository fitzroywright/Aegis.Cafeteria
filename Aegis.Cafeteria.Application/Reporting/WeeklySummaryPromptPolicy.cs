namespace Aegis.Cafeteria.Application.Reporting;

public sealed record WeeklySummaryPromptDecision
(
      bool ShouldPrompt
    , string? SuggestedEmail
);

public static class WeeklySummaryPromptPolicy
{
    public static WeeklySummaryPromptDecision Evaluate
    (
          CustomerAccountView account
        , bool preferenceAsked
        , string? savedEmailAddress
        , bool authoritativeEmail = false
    )
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit || preferenceAsked)
        {
            return new WeeklySummaryPromptDecision(false, null);
        }

        string? suggested = !string.IsNullOrWhiteSpace(savedEmailAddress)
            ? savedEmailAddress.Trim()
            : authoritativeEmail || string.IsNullOrWhiteSpace(account.Customer.EmailAddress)
                ? null
                : account.Customer.EmailAddress.Trim();

        return new WeeklySummaryPromptDecision(true, suggested);
    }
}

