namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

public sealed record CustomerNotificationPreferenceRequest
(
      bool WeeklySummaryEnabled
    , string? EmailAddress
);

public sealed record ManagerReportSettingsRequest
(
      bool WeeklySummaryEnabled
    , string? EmailAddress
    , DayOfWeek WeeklySummaryDay
    , int WeeklySummaryHourLocal
);

public sealed record TestEmailRequest(string EmailAddress);

public static class EmailEndpoints
{
    public static void Map(WebApplication app)
    {
        AuditTrailEndpoints.Map(app);

        app.MapGet
        (
              "/api/pos/customers/{customerId:int}/notification-preference"
            , async (
                  int customerId
                , IDbContextFactory<ServiceDbContext> factory
                , CancellationToken cancellationToken) =>
            {
                NotificationPreferenceStore store = new(factory);
                CustomerNotificationPreference preference = await store.GetCustomerAsync(customerId, cancellationToken);
                return Results.Ok(preference);
            }
        );

        app.MapPut
        (
              "/api/pos/customers/{customerId:int}/notification-preference"
            , async (int customerId, CustomerNotificationPreferenceRequest request, IDbContextFactory<ServiceDbContext> factory, CancellationToken cancellationToken) =>
            {
                try
                {
                    NotificationPreferenceStore store = new(factory);
                    CustomerNotificationPreference preference = await store.SetCustomerAsync
                    (
                          customerId
                        , request.WeeklySummaryEnabled
                        , request.EmailAddress
                        , cancellationToken
                    );
                    return Results.Ok(preference);
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }
            }
        );

        app.MapGet
        (
              "/api/management/report-settings"
            , async (IDbContextFactory<ServiceDbContext> factory, CancellationToken cancellationToken) =>
            {
                NotificationPreferenceStore store = new(factory);
                return Results.Ok(await store.GetManagerAsync(cancellationToken));
            }
        ).RequireAuthorization("CafeteriaManagement");

        app.MapPut
        (
              "/api/management/report-settings"
            , async (ManagerReportSettingsRequest request, IDbContextFactory<ServiceDbContext> factory, CancellationToken cancellationToken) =>
            {
                try
                {
                    NotificationPreferenceStore store = new(factory);
                    ManagerReportSettings settings = await store.SetManagerAsync
                    (
                          request.WeeklySummaryEnabled
                        , request.EmailAddress
                        , request.WeeklySummaryDay
                        , request.WeeklySummaryHourLocal
                        , cancellationToken
                    );
                    return Results.Ok(settings);
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }
            }
        ).RequireAuthorization("CafeteriaManagement");

        app.MapPost
        (
              "/api/management/report-settings/test-email"
            , async (TestEmailRequest request, IDbContextFactory<ServiceDbContext> factory, CancellationToken cancellationToken) =>
            {
                try
                {
                    string recipient = EmailAddressValidator.Normalize(request.EmailAddress)
                        ?? throw new ArgumentException("A valid manager email address is required.", nameof(request.EmailAddress));
                    OutboundEmailQueue queue = new(factory);
                    await queue.EnqueueAsync
                    (
                          $"test-email:{Guid.NewGuid():N}"
                        , recipient
                        , "Aegis.Cafeteria Test Email"
                        , "This is a test message from Aegis.Cafeteria. Email delivery is configured correctly if you received this message."
                        , cancellationToken
                    );
                    return Results.Accepted();
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }
            }
        ).RequireAuthorization("CafeteriaManagement");
    }
}
