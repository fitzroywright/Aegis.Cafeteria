namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;
using System.Text.Json;

public sealed record CustomerNotificationPreference(int CustomerId, bool PreferenceAsked, bool WeeklySummaryEnabled, string? EmailAddress);
public sealed record ManagerReportSettings(bool WeeklySummaryEnabled, string? EmailAddress, DayOfWeek WeeklySummaryDay, int WeeklySummaryHourLocal);

public static class EmailAddressValidator
{
    public static string? Normalize(string? emailAddress)
    {
        if (string.IsNullOrWhiteSpace(emailAddress))
        {
            return null;
        }

        try
        {
            MailAddress address = new(emailAddress.Trim());
            return address.Address;
        }
        catch (FormatException)
        {
            throw new ArgumentException("A valid email address is required.", nameof(emailAddress));
        }
    }
}

public sealed class NotificationPreferenceStore(IDbContextFactory<ServiceDbContext> factory)
{
    public async Task<CustomerNotificationPreference> GetCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        CustomerNotificationPreferenceRow? row = await database.CustomerNotificationPreferences.FindAsync([customerId], cancellationToken);
        return row is null
            ? new CustomerNotificationPreference(customerId, false, false, null)
            : new CustomerNotificationPreference(row.CustomerId, row.PreferenceAsked, row.WeeklySummaryEnabled, row.EmailAddress);
    }

    public async Task<CustomerNotificationPreference> SetCustomerAsync(int customerId, bool enabled, string? emailAddress, CancellationToken cancellationToken = default)
    {
        string? normalizedEmail = enabled ? EmailAddressValidator.Normalize(emailAddress) : null;
        if (enabled && normalizedEmail is null)
        {
            throw new ArgumentException("An email address is required when weekly summaries are enabled.", nameof(emailAddress));
        }

        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        CustomerNotificationPreferenceRow? row = await database.CustomerNotificationPreferences.FindAsync([customerId], cancellationToken);
        if (row is null)
        {
            row = new CustomerNotificationPreferenceRow { CustomerId = customerId };
            database.CustomerNotificationPreferences.Add(row);
        }

        row.PreferenceAsked = true;
        row.WeeklySummaryEnabled = enabled;
        row.EmailAddress = normalizedEmail;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return new CustomerNotificationPreference(row.CustomerId, row.PreferenceAsked, row.WeeklySummaryEnabled, row.EmailAddress);
    }

    public async Task<ManagerReportSettings> GetManagerAsync(CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        ManagerReportSettingsRow? row = await database.ManagerReportSettings.FindAsync([1], cancellationToken);
        return row is null
            ? new ManagerReportSettings(false, null, DayOfWeek.Monday, 8)
            : new ManagerReportSettings(row.WeeklySummaryEnabled, row.EmailAddress, row.WeeklySummaryDay, row.WeeklySummaryHourLocal);
    }

    public async Task<ManagerReportSettings> SetManagerAsync(bool enabled, string? emailAddress, DayOfWeek day, int hourLocal, CancellationToken cancellationToken = default)
    {
        if (hourLocal is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(hourLocal), "Report hour must be between 0 and 23.");
        }

        string? normalizedEmail = EmailAddressValidator.Normalize(emailAddress);
        if (enabled && normalizedEmail is null)
        {
            throw new ArgumentException("An email address is required when manager summaries are enabled.", nameof(emailAddress));
        }

        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        ManagerReportSettingsRow? row = await database.ManagerReportSettings.FindAsync([1], cancellationToken);
        if (row is null)
        {
            row = new ManagerReportSettingsRow { Id = 1 };
            database.ManagerReportSettings.Add(row);
        }

        row.WeeklySummaryEnabled = enabled;
        row.EmailAddress = normalizedEmail;
        row.WeeklySummaryDay = day;
        row.WeeklySummaryHourLocal = hourLocal;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        return new ManagerReportSettings(row.WeeklySummaryEnabled, row.EmailAddress, row.WeeklySummaryDay, row.WeeklySummaryHourLocal);
    }
}

public sealed class OutboundEmailQueue(IDbContextFactory<ServiceDbContext> factory)
{
    public Task EnqueueAsync(string dispatchKey, string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        return EnqueueAsync(dispatchKey, recipient, subject, body, [], cancellationToken);
    }

    public async Task EnqueueAsync(
        string dispatchKey,
        string recipient,
        string subject,
        string body,
        IReadOnlyCollection<EmailAttachment> attachments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchKey);
        ArgumentNullException.ThrowIfNull(attachments);

        string normalizedRecipient = EmailAddressValidator.Normalize(recipient)
            ?? throw new ArgumentException("A recipient is required.", nameof(recipient));
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        if (await database.OutboundEmails.AnyAsync(row => row.DispatchKey == dispatchKey, cancellationToken))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        database.OutboundEmails.Add(new OutboundEmailRow
        {
            Id = Guid.NewGuid(),
            DispatchKey = dispatchKey,
            Recipient = normalizedRecipient,
            Subject = subject,
            Body = DurableEmailPayloadCodec.Encode(body, attachments),
            CreatedAt = now,
            NextAttemptAt = now
        });
        await database.SaveChangesAsync(cancellationToken);
    }
}

public sealed class OutboundEmailWorker(
    IDbContextFactory<ServiceDbContext> factory,
    CommonMessagingEmailSender sender,
    EmailDeliveryOptions deliveryOptions,
    ILogger<OutboundEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Cafeteria outbound email worker failed; queued mail remains durable");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public async Task DeliverPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!deliveryOptions.Enabled)
        {
            return;
        }
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // SQLite cannot reliably translate every DateTimeOffset comparison used here.
        // Keep the durable filtering server-side where possible and evaluate due-time ordering in memory.
        OutboundEmailRow[] unsent = await database.OutboundEmails
            .Where(row => row.SentAt == null)
            .ToArrayAsync(cancellationToken);
        OutboundEmailRow[] pending = unsent
            .Where(row => row.NextAttemptAt <= now)
            .OrderBy(row => row.CreatedAt)
            .Take(25)
            .ToArray();

        foreach (OutboundEmailRow row in pending)
        {
            row.AttemptCount++;
            row.LastAttemptAt = now;
            try
            {
                DurableEmailPayload payload = DurableEmailPayloadCodec.Decode(row.Body);
                string messageId = await sender.SendAsync(
                    row.Id,
                    row.Recipient,
                    row.Subject,
                    payload.Body,
                    payload.Attachments,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(messageId))
                {
                    row.SentAt = DateTimeOffset.UtcNow;
                    row.MessageId = messageId;
                    row.LastError = null;
                }
                else
                {
                    row.LastError = "Common.Messaging did not confirm provider acceptance.";
                    row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, Math.Max(1, row.AttemptCount * 5)));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                row.LastError = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
                row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, Math.Max(1, row.AttemptCount * 5)));
                logger.LogWarning(exception, "Cafeteria email delivery failed for {DispatchKey}; retry scheduled", row.DispatchKey);
            }

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}

internal sealed record DurableEmailPayload(string Body, EmailAttachment[] Attachments);

internal static class DurableEmailPayloadCodec
{
    private const string Prefix = "AEGIS-EMAIL-ENVELOPE-V1:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Encode(string body, IReadOnlyCollection<EmailAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return body;
        }

        DurableEmailPayload payload = new(body, attachments.ToArray());
        return Prefix + JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static DurableEmailPayload Decode(string persistedBody)
    {
        if (!persistedBody.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return new DurableEmailPayload(persistedBody, []);
        }

        DurableEmailPayload? payload = JsonSerializer.Deserialize<DurableEmailPayload>(
            persistedBody[Prefix.Length..],
            JsonOptions);
        return payload ?? throw new InvalidOperationException("Durable email payload could not be decoded.");
    }
}
