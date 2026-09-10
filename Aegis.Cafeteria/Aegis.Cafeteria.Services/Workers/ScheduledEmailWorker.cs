namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

public sealed class ScheduledEmailWorker : BackgroundService
{
    private readonly ScheduledEmailOptions options;
    private readonly FfpEmailReportSource source;
    private readonly PayrollDeductionReportSource payrollSource;
    private readonly NotificationPreferenceStore preferences;
    private readonly OutboundEmailQueue queue;
    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly ILogger<ScheduledEmailWorker> logger;

    public ScheduledEmailWorker(
        ScheduledEmailOptions options,
        FfpEmailReportSource source,
        PayrollDeductionReportSource payrollSource,
        SmtpEmailSender sender,
        IDbContextFactory<ServiceDbContext> factory,
        ILogger<ScheduledEmailWorker> logger)
    {
        this.options = options;
        this.source = source;
        this.payrollSource = payrollSource;
        this.factory = factory;
        this.logger = logger;
        this.preferences = new NotificationPreferenceStore(factory);
        this.queue = new OutboundEmailQueue(factory);
        _ = sender; // Retained in the constructor for binary/source compatibility; delivery belongs to OutboundEmailWorker.
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (this.options.Enabled)
                {
                    DateTime now = DateTime.Now;
                    DateOnly today = DateOnly.FromDateTime(now.Date);
                    await QueueDailyOnceAsync(today.AddDays(-1), stoppingToken);
                    if (now.Hour >= this.options.DailySummaryHourLocal)
                    {
                        await QueueDailyOnceAsync(today, stoppingToken);
                    }

                    int daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                    int employeeOffset = ((int)this.options.WeeklyEmployeeDay - (int)DayOfWeek.Monday + 7) % 7;
                    if (daysSinceMonday > employeeOffset ||
                        (daysSinceMonday == employeeOffset && now.Hour >= this.options.WeeklyEmployeeHourLocal))
                    {
                        await QueuePreviousWeekAsync(today, stoppingToken);
                    }

                    ManagerReportSettings manager = await this.preferences.GetManagerAsync(stoppingToken);
                    int managerOffset = ((int)manager.WeeklySummaryDay - (int)DayOfWeek.Monday + 7) % 7;
                    if (manager.WeeklySummaryEnabled &&
                        (daysSinceMonday > managerOffset ||
                         (daysSinceMonday == managerOffset && now.Hour >= manager.WeeklySummaryHourLocal)))
                    {
                        await QueueManagerPreviousWeekAsync(today, manager, stoppingToken);
                    }

                    if (this.options.PayrollNotificationEnabled && now.Hour >= this.options.PayrollReportHourLocal)
                    {
                        DateOnly previousMonth = today.AddDays(1 - today.Day).AddMonths(-1);
                        await SendPayrollReportAsync(previousMonth.Year, previousMonth.Month, false, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                this.logger.LogError(
                    exception,
                    "Cafeteria scheduled report coordinator failed; the always-on service will retry scheduling on the next cycle");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public async Task<PayrollDeductionReportPackage> SendPayrollReportAsync(
        int year,
        int month,
        bool force,
        CancellationToken cancellationToken = default)
    {
        PayrollDeductionReportPackage report = await this.payrollSource.BuildAsync(year, month, cancellationToken);
        if (this.options.PayrollRecipients.Length == 0)
        {
            return report;
        }

        string periodName = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        StringBuilder body = new();
        body.AppendLine($"Cafeteria employee deduction notification for {periodName}").AppendLine();
        body.AppendLine($"Employees requiring deduction: {report.EmployeesWithAmountDue:N0}");
        body.AppendLine($"Total Employee Amount Due: {report.TotalEmployeeAmountDue:N2}").AppendLine();
        body.AppendLine("Cafeteria reports the amount due only. Payroll remains responsible for deduction, collection and reconciliation.");

        EmailAttachment[] attachments =
        [
            new(
                $"Cafeteria-Payroll-Detail-{year:D4}-{month:D2}.csv",
                "text/csv",
                PayrollDeductionReportSource.BuildDetailCsv(report)),
            new(
                $"Cafeteria-Payroll-Summary-{year:D4}-{month:D2}.csv",
                "text/csv",
                PayrollDeductionReportSource.BuildSummaryCsv(report))
        ];

        foreach (string configuredRecipient in this.options.PayrollRecipients)
        {
            string? recipient = EmailAddressValidator.Normalize(configuredRecipient);
            if (recipient is null)
            {
                continue;
            }

            string stableKey = $"payroll:{year:D4}-{month:D2}:{recipient.ToLowerInvariant()}";
            string dispatchKey = force
                ? $"{stableKey}:manual:{Guid.NewGuid():N}"
                : stableKey;

            await this.queue.EnqueueAsync(
                dispatchKey,
                recipient,
                $"Cafeteria Employee Amount Due - {periodName}",
                body.ToString(),
                attachments,
                cancellationToken);
        }

        return report;
    }

    private async Task QueueDailyOnceAsync(DateOnly date, CancellationToken cancellationToken)
    {
        if (this.options.DailySummaryRecipients.Length == 0)
        {
            return;
        }

        DailyLedgerSummary summary = await this.source.GetDailySummaryAsync(date, cancellationToken);
        string body = $"Cafeteria Daily Summary - {date:dddd, MMMM d, yyyy}\n\nTransactions: {summary.Transactions}\nPurchases: {summary.Purchases:N2}\nRefunds: {summary.Refunds:N2}\nAdditional Credit: {summary.AdditionalCredit:N2}\nStipend Card Reductions: {summary.StipendReductions:N2}\n";

        foreach (string configuredRecipient in this.options.DailySummaryRecipients)
        {
            string? recipient = EmailAddressValidator.Normalize(configuredRecipient);
            if (recipient is null)
            {
                continue;
            }

            string key = $"daily:{date:yyyy-MM-dd}:{recipient.ToLowerInvariant()}";
            await this.queue.EnqueueAsync(
                key,
                recipient,
                $"Cafeteria Daily Summary - {date:yyyy-MM-dd}",
                body,
                cancellationToken);
        }
    }

    private async Task QueuePreviousWeekAsync(DateOnly today, CancellationToken cancellationToken)
    {
        (DateOnly start, DateOnly end) = PreviousWeek(today);
        IReadOnlyList<EmployeeWeeklyLedger> ledgers = await this.source.GetWeeklyEmployeeLedgersAsync(start, end, cancellationToken);
        foreach (EmployeeWeeklyLedger employee in ledgers)
        {
            CustomerNotificationPreference preference = await this.preferences.GetCustomerAsync(employee.EmployeeId, cancellationToken);
            if (!preference.WeeklySummaryEnabled || string.IsNullOrWhiteSpace(preference.EmailAddress))
            {
                continue;
            }

            await this.queue.EnqueueAsync(
                $"weekly:{employee.EmployeeId}:{start:yyyy-MM-dd}",
                preference.EmailAddress,
                $"Your Cafeteria Transactions - Week ending {end:yyyy-MM-dd}",
                BuildEmployeeBody(employee, start, end),
                cancellationToken);
        }
    }

    private async Task QueueManagerPreviousWeekAsync(
        DateOnly today,
        ManagerReportSettings manager,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manager.EmailAddress))
        {
            return;
        }

        (DateOnly start, DateOnly end) = PreviousWeek(today);
        IReadOnlyList<EmployeeWeeklyLedger> ledgers = await this.source.GetWeeklyEmployeeLedgersAsync(start, end, cancellationToken);
        int transactionCount = ledgers.Sum(employee => employee.Lines.Count);
        decimal netValue = ledgers.Sum(employee => employee.Lines.Sum(line => line.ScanValue));
        string body = $"Cafeteria weekly management summary - {start:MMM d} - {end:MMM d, yyyy}\n\nEmployees with activity: {ledgers.Count:N0}\nLedger entries: {transactionCount:N0}\nNet ledger value: {netValue:N2}\n";
        await this.queue.EnqueueAsync(
            $"manager-weekly:{start:yyyy-MM-dd}",
            manager.EmailAddress,
            $"Cafeteria Weekly Summary - Week ending {end:yyyy-MM-dd}",
            body,
            cancellationToken);
    }

    private static string BuildEmployeeBody(EmployeeWeeklyLedger employee, DateOnly start, DateOnly end)
    {
        StringBuilder body = new();
        body.AppendLine($"Cafeteria transactions for {start:MMM d} - {end:MMM d, yyyy}").AppendLine();
        foreach (EmployeeWeeklyLedgerLine line in employee.Lines)
        {
            body.AppendLine($"{line.ScanDate:g} | {line.IdName} | {line.ScanValue:N2} | {line.Description}");
        }

        return body.ToString();
    }

    private static (DateOnly Start, DateOnly End) PreviousWeek(DateOnly today)
    {
        int daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateOnly start = today.AddDays(-daysSinceMonday).AddDays(-7);
        return (start, start.AddDays(6));
    }

    // Retained for compatibility with existing scheduled-dispatch history. New queued email work
    // uses OutboundEmails.DispatchKey as the durable idempotency record.
    private async Task<bool> WasSentAsync(string key, CancellationToken cancellationToken)
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        return await database.ScheduledDispatches.AnyAsync(row => row.DispatchKey == key, cancellationToken);
    }

    private async Task MarkSentAsync(string key, string messageId, CancellationToken cancellationToken)
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        if (!await database.ScheduledDispatches.AnyAsync(row => row.DispatchKey == key, cancellationToken))
        {
            database.ScheduledDispatches.Add(new ScheduledDispatchRow
            {
                DispatchKey = key,
                SentAt = DateTimeOffset.UtcNow,
                MessageId = messageId
            });
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
