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
public sealed record DailySalesReportRequest
(
      DateOnly Date
    , string[]? Recipients = null
    , string? AttachmentFileName = null
    , string? AttachmentContentType = null
    , byte[]? AttachmentContent = null
);
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
                , HttpContext http
                , CafeteriaEmailReportSource reports
                , IDbContextFactory<ServiceDbContext> factory
                , CancellationToken cancellationToken) =>
            {
                if (request.Date > DateOnly.FromDateTime(DateTime.Now))
                {
                    return Results.BadRequest(new { error = "The Daily Sales Report date cannot be in the future." });
                }

                NotificationPreferenceStore settingsStore = new(factory);
                ManagerReportSettings settings = await settingsStore.GetManagerAsync(cancellationToken);

                string[] recipients;
                try
                {
                    recipients = (request.Recipients ?? [])
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(EmailAddressValidator.Normalize)
                        .Where(value => value is not null)
                        .Select(value => value!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (recipients.Length == 0)
                    {
                        string? defaultRecipient = EmailAddressValidator.Normalize(settings.EmailAddress);
                        recipients = defaultRecipient is null ? [] : [defaultRecipient];
                    }
                }
                catch (ArgumentException exception)
                {
                    return Results.BadRequest(new { error = exception.Message });
                }

                if (recipients.Length == 0)
                {
                    return Results.BadRequest(new { error = "Enter at least one recipient or configure the Manager email address." });
                }

                EmailAttachment[] attachments = [];
                if (request.AttachmentContent is { Length: > 0 })
                {
                    if (request.AttachmentContent.Length > 10 * 1024 * 1024)
                    {
                        return Results.BadRequest(new { error = "The report attachment exceeds the 10 MB email limit." });
                    }

                    string fileName = string.IsNullOrWhiteSpace(request.AttachmentFileName)
                        ? $"Cafeteria-Daily-Sales-{request.Date:yyyy-MM-dd}.pdf"
                        : Path.GetFileName(request.AttachmentFileName.Trim());
                    string contentType = string.IsNullOrWhiteSpace(request.AttachmentContentType)
                        ? "application/pdf"
                        : request.AttachmentContentType.Trim();

                    attachments =
                    [
                        new EmailAttachment(fileName, contentType, request.AttachmentContent)
                    ];
                }

                DailyLedgerSummary summary = await reports.GetDailySummaryAsync(request.Date, cancellationToken);

                await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
                int pending = await database.Events.CountAsync(row => row.ExternalPublishedAt == null, cancellationToken);
                int failed = await database.Events.CountAsync(row => row.PublicationLastError != null, cancellationToken);
                DateTimeOffset? lastSuccessfulSync = await database.Events
                    .Where(row => row.ExternalPublishedAt != null)
                    .OrderByDescending(row => row.ExternalPublishedAt)
                    .Select(row => row.ExternalPublishedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                string? lastError = await database.Events
                    .Where(row => row.PublicationLastError != null)
                    .OrderByDescending(row => row.PublicationLastAttemptAt)
                    .Select(row => row.PublicationLastError)
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
                string requestId = Guid.NewGuid().ToString("N");

                foreach (string recipient in recipients)
                {
                    await queue.EnqueueAsync
                    (
                          $"daily-sales-manual:{request.Date:yyyyMMdd}:{requestId}:{recipient.ToLowerInvariant()}"
                        , recipient
                        , $"Cafeteria Daily Sales Report - {request.Date:yyyy-MM-dd}"
                        , body
                        , attachments
                        , cancellationToken
                    );
                }

                string actor = http.User.Identity?.Name ?? "SYSTEM";
                AuditTrailService audit = new(factory);
                await audit.RecordAsync
                (
                      actor
                    , "DailySalesReportEmailRequested"
                    , "DailySalesReport"
                    , request.Date.ToString("yyyy-MM-dd")
                    , source: "Aegis.Cafeteria.Services"
                    , succeeded: true
                    , statusCode: StatusCodes.Status202Accepted
                    , after: new
                    {
                          request.Date
                        , Recipients = recipients
                        , ReportStatus = reportStatus
                        , HasAttachment = attachments.Length > 0
                    }
                    , detail: $"Daily Sales Report queued for {recipients.Length} recipient(s)."
                    , cancellationToken: cancellationToken
                );

                return Results.Accepted
                (
                    value: new
                    {
                          date = request.Date
                        , recipients
                        , reportStatus
                        , lastSuccessfulSync
                        , pendingSyncOperations = pending
                        , failedSyncOperations = failed
                        , attachmentIncluded = attachments.Length > 0
                    }
                );
            }
        ).RequireAuthorization("CafeteriaManagement");
    }
}
