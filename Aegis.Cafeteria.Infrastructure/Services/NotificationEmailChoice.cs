namespace Aegis.Cafeteria.Infrastructure.Services;

public static class NotificationEmailChoice
{
    public static string? AcknowledgedSource(CustomerNotificationPreferenceSnapshot preference, bool enabled, string? address)
    {
        if (!enabled || preference.SuggestedEmailSource is null)
        {
            return null;
        }

        bool acceptsSuggestion = string.Equals(address?.Trim(), preference.SuggestedEmailAddress?.Trim(), StringComparison.OrdinalIgnoreCase);
        bool keepsReviewedAddress = (preference.NeedsEmailReview || preference.NotificationEmailSource is not null)
            && string.Equals(address?.Trim(), preference.EmailAddress?.Trim(), StringComparison.OrdinalIgnoreCase);
        return acceptsSuggestion || keepsReviewedAddress ? preference.SuggestedEmailSource : null;
    }
}
