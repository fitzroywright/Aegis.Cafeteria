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
public sealed record DailySalesReportRequest(DateOnly Date);
public sealed record ManagerWeeklySummaryRequest(DateOnly WeekStart);

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

        app.MapPost
        (
              "/api/management/reports/weekly-manager/send"
            , async (
                  ManagerWeeklySummaryRequest request
                , Aegis.Cafeteria.Services.Workers.ScheduledEmailWorker worker
                , CancellationToken cancellationToken) =>
            {
                if (request.WeekStart.DayOfWeek != DayOfWeek.Monday)
                {
                    return Results.BadRequest(new { error = "Manager weekly summary start date must be a Monday." });
                }

                if (request.WeekStart >= DateOnly.FromDateTime(DateTime.Today))
                {
                    return Results.BadRequest(new { error = "Only a completed week can be resent." });
                }

                try
                {
                    await worker.ResendManagerWeeklySummaryAsync(request.WeekStart, cancellationToken);
                    return Results.Accepted();
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(new { error = exception.Message });
                }
            }
        ).RequireAuthorization("CafeteriaManagement");

        app.MapPost
        (
              "/api/management/reports/daily-sales/send"
            , async (
                  DailySalesReportRequest request
                , FfpEmailReportSource reports
                , IDbContextFactory<ServiceDbContext> factory
                , CancellationToken cancellationToken) =>
            {
                if (request.Date > DateOnly.FromDateTime(DateTime.Now))
                {
                    return Results.BadRequest(new { error = "The Daily Sales Report date cannot be in the future." });
                }

                NotificationPreferenceStore settingsStore = new(factory);
                ManagerReportSettings settings = await settingsStore.GetManagerAsync(cancellationToken);
                string recipient = EmailAddressValidator.Normalize(settings.EmailAddress)
                    ?? string.Empty;
                if (recipient.Length == 0)
                {
                    return Results.BadRequest(new { error = "Configure the Manager email in Email & Reports before sending a Daily Sales Report." });
                }

                DailyLedgerSummary summary = await reports.GetDailySummaryAsync(request.Date, cancellationToken);

                await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
                int pending = await database.Events.CountAsync(row => row.FfpPublishedAt == null, cancellationToken);
                int failed = await database.Events.CountAsync(row => row.FfpLastError != null, cancellationToken);
                DateTimeOffset? lastSuccessfulSync = await database.Events
                    .Where(row => row.FfpPublishedAt != null)
                    .OrderByDescending(row => row.FfpPublishedAt)
                    .Select(row => row.FfpPublishedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                string? lastError = await database.Events
                    .Where(row => row.FfpLastError != null)
                    .OrderByDescending(row => row.FfpLastAttemptAt)
                    .Select(row => row.FfpLastError)
                    .FirstOrDefaultAsync(cancellationToken);

                bool complete = pending == 0 && failed == 0;
                string reportStatus = complete ? "Complete" : "Data Incomplete";
                string lastSyncText = lastSuccessfulSync?.ToLocalTime().ToString("yyyy-MM-dd h:mm tt") ?? "Not yet available";

                string body =
                    $"Cafeteria Daily Sales Report\n" +
                    $"Date: {request.Date:yyyy-MM-dd}\n\n" +
                    $"Report Status: {reportStatus}\n" +
                    $"Last Successful Sync: {lastSyncText}\n" +
                    (complete ? string.Empty : $"Pending Sync Operations: {pending}\nFailed Sync Operations: {failed}\n") +
                    (!complete && !string.IsNullOrWhiteSpace(lastError) ? $"Last Sync Error: {lastError}\n" : string.Empty) +
                    $"\nTransactions: {summary.Transactions}\n" +
                    $"Purchases: {summary.Purchases:C}\n" +
                    $"Refunds: {summary.Refunds:C}\n" +
                    $"Additional Credit: {summary.AdditionalCredit:C}\n" +
                    $"Stipend Card Reductions: {summary.StipendReductions:C}\n";

                OutboundEmailQueue queue = new(factory);
                await queue.EnqueueAsync
                (
                      $"daily-sales-manual:{request.Date:yyyyMMdd}:{Guid.NewGuid():N}"
                    , recipient
                    , $"Cafeteria Daily Sales Report - {request.Date:yyyy-MM-dd}"
                    , body
                    , cancellationToken
                );

                return Results.Accepted
                (
                    value: new
                    {
                          date = request.Date
                        , recipient
                        , reportStatus
                        , lastSuccessfulSync
                        , pendingSyncOperations = pending
                        , failedSyncOperations = failed
                    }
                );
            }
        ).RequireAuthorization("CafeteriaManagement");
    }
}
