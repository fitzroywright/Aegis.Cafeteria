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
    private readonly SmtpEmailSender sender;
    private readonly NotificationPreferenceStore preferences;
    private readonly OutboundEmailQueue queue;
    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly ILogger<ScheduledEmailWorker> logger;

    public ScheduledEmailWorker(ScheduledEmailOptions options, FfpEmailReportSource source, PayrollDeductionReportSource payrollSource, SmtpEmailSender sender, IDbContextFactory<ServiceDbContext> factory, ILogger<ScheduledEmailWorker> logger)
    {
        this.options = options;
        this.source = source;
        this.payrollSource = payrollSource;
        this.sender = sender;
        this.factory = factory;
        this.logger = logger;
        this.preferences = new NotificationPreferenceStore(factory);
        this.queue = new OutboundEmailQueue(factory);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverQueuedAsync(stoppingToken);

                if (this.options.Enabled)
                {
                    DateTime now = DateTime.Now;
                    DateOnly today = DateOnly.FromDateTime(now.Date);
                    await SendDailyOnceAsync(today.AddDays(-1), stoppingToken);
                    if (now.Hour >= this.options.DailySummaryHourLocal) await SendDailyOnceAsync(today, stoppingToken);
                    int daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                    int employeeOffset = ((int)this.options.WeeklyEmployeeDay - (int)DayOfWeek.Monday + 7) % 7;
                    if (daysSinceMonday > employeeOffset || (daysSinceMonday == employeeOffset && now.Hour >= this.options.WeeklyEmployeeHourLocal)) await QueuePreviousWeekAsync(today, stoppingToken);
                    ManagerReportSettings manager = await this.preferences.GetManagerAsync(stoppingToken);
                    int managerOffset = ((int)manager.WeeklySummaryDay - (int)DayOfWeek.Monday + 7) % 7;
                    if (manager.WeeklySummaryEnabled && (daysSinceMonday > managerOffset || (daysSinceMonday == managerOffset && now.Hour >= manager.WeeklySummaryHourLocal))) await QueueManagerPreviousWeekAsync(today, manager, stoppingToken);
                    if (this.options.PayrollNotificationEnabled && now.Hour >= this.options.PayrollReportHourLocal)
                    {
                        DateOnly previousMonth = today.AddDays(1 - today.Day).AddMonths(-1);
                        await SendPayrollReportAsync(previousMonth.Year, previousMonth.Month, false, stoppingToken);
                    }
                    await DeliverQueuedAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { this.logger.LogError(exception, "Cafeteria email worker failed; queued mail remains durable and will retry"); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public async Task<PayrollDeductionReportPackage> SendPayrollReportAsync(int year, int month, bool force, CancellationToken cancellationToken = default)
    {
        string key = $"payroll:{year:D4}-{month:D2}";
        PayrollDeductionReportPackage report = await this.payrollSource.BuildAsync(year, month, cancellationToken);
        if (!force && await WasSentAsync(key, cancellationToken)) return report;
        if (this.options.PayrollRecipients.Length == 0) return report;
        string periodName = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        StringBuilder body = new();
        body.AppendLine($"Cafeteria employee deduction notification for {periodName}").AppendLine();
        body.AppendLine($"Employees requiring deduction: {report.EmployeesWithAmountDue:N0}");
        body.AppendLine($"Total Employee Amount Due: {report.TotalEmployeeAmountDue:N2}").AppendLine();
        body.AppendLine("Cafeteria reports the amount due only. Payroll remains responsible for deduction, collection and reconciliation.");
        EmailAttachment[] attachments = [new($"Cafeteria-Payroll-Detail-{year:D4}-{month:D2}.csv", "text/csv", PayrollDeductionReportSource.BuildDetailCsv(report)), new($"Cafeteria-Payroll-Summary-{year:D4}-{month:D2}.csv", "text/csv", PayrollDeductionReportSource.BuildSummaryCsv(report))];
        string messageId = await this.sender.SendAsync(this.options.PayrollRecipients, $"Cafeteria Employee Amount Due - {periodName}", body.ToString(), attachments, cancellationToken);
        if (!string.IsNullOrWhiteSpace(messageId)) await MarkSentAsync(key, messageId, cancellationToken);
        return report;
    }

    private async Task SendDailyOnceAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string key = $"daily:{date:yyyy-MM-dd}";
        if (await WasSentAsync(key, cancellationToken)) return;
        DailyLedgerSummary summary = await this.source.GetDailySummaryAsync(date, cancellationToken);
        string body = $"Cafeteria Daily Summary - {date:dddd, MMMM d, yyyy}\n\nTransactions: {summary.Transactions}\nPurchases: {summary.Purchases:N2}\nRefunds: {summary.Refunds:N2}\nAdditional Credit: {summary.AdditionalCredit:N2}\nStipend Card Reductions: {summary.StipendReductions:N2}\n";
        string messageId = await this.sender.SendAsync(this.options.DailySummaryRecipients, $"Cafeteria Daily Summary - {date:yyyy-MM-dd}", body, cancellationToken);
        if (!string.IsNullOrWhiteSpace(messageId)) await MarkSentAsync(key, messageId, cancellationToken);
    }

    private async Task QueuePreviousWeekAsync(DateOnly today, CancellationToken cancellationToken)
    {
        (DateOnly start, DateOnly end) = PreviousWeek(today);
        IReadOnlyList<EmployeeWeeklyLedger> ledgers = await this.source.GetWeeklyEmployeeLedgersAsync(start, end, cancellationToken);
        foreach (EmployeeWeeklyLedger employee in ledgers)
        {
            CustomerNotificationPreference preference = await this.preferences.GetCustomerAsync(employee.EmployeeId, cancellationToken);
            if (!preference.WeeklySummaryEnabled || string.IsNullOrWhiteSpace(preference.EmailAddress)) continue;
            await this.queue.EnqueueAsync($"weekly:{employee.EmployeeId}:{start:yyyy-MM-dd}", preference.EmailAddress, $"Your Cafeteria Transactions - Week ending {end:yyyy-MM-dd}", BuildEmployeeBody(employee, start, end), cancellationToken);
        }
    }

    private async Task QueueManagerPreviousWeekAsync(DateOnly today, ManagerReportSettings manager, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manager.EmailAddress)) return;
        (DateOnly start, DateOnly end) = PreviousWeek(today);
        IReadOnlyList<EmployeeWeeklyLedger> ledgers = await this.source.GetWeeklyEmployeeLedgersAsync(start, end, cancellationToken);
        int transactionCount = ledgers.Sum(employee => employee.Lines.Count);
        decimal netValue = ledgers.Sum(employee => employee.Lines.Sum(line => line.ScanValue));
        string body = $"Cafeteria weekly management summary - {start:MMM d} - {end:MMM d, yyyy}\n\nEmployees with activity: {ledgers.Count:N0}\nLedger entries: {transactionCount:N0}\nNet ledger value: {netValue:N2}\n";
        await this.queue.EnqueueAsync($"manager-weekly:{start:yyyy-MM-dd}", manager.EmailAddress, $"Cafeteria Weekly Summary - Week ending {end:yyyy-MM-dd}", body, cancellationToken);
    }

    private async Task DeliverQueuedAsync(CancellationToken cancellationToken)
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        OutboundEmailRow[] pending = await database.OutboundEmails.Where(row => row.SentAt == null && row.NextAttemptAt <= now).OrderBy(row => row.CreatedAt).Take(25).ToArrayAsync(cancellationToken);
        foreach (OutboundEmailRow row in pending)
        {
            row.AttemptCount++;
            row.LastAttemptAt = now;
            try
            {
                string messageId = await this.sender.SendAsync([row.Recipient], row.Subject, row.Body, cancellationToken);
                if (string.IsNullOrWhiteSpace(messageId)) throw new InvalidOperationException("SMTP sender did not return a message identifier.");
                row.SentAt = DateTimeOffset.UtcNow;
                row.MessageId = messageId;
                row.LastError = null;
            }
            catch (Exception exception)
            {
                row.LastError = exception.Message.Length <= 2000 ? exception.Message : exception.Message[..2000];
                row.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, Math.Max(1, row.AttemptCount * 5)));
                this.logger.LogWarning(exception, "Cafeteria email delivery failed for {DispatchKey}; retry scheduled", row.DispatchKey);
            }
            await database.SaveChangesAsync(cancellationToken);
        }
    }

    private static string BuildEmployeeBody(EmployeeWeeklyLedger employee, DateOnly start, DateOnly end)
    {
        StringBuilder body = new();
        body.AppendLine($"Cafeteria transactions for {start:MMM d} - {end:MMM d, yyyy}").AppendLine();
        foreach (EmployeeWeeklyLedgerLine line in employee.Lines) body.AppendLine($"{line.ScanDate:g} | {line.IdName} | {line.ScanValue:N2} | {line.Description}");
        return body.ToString();
    }

    private static (DateOnly Start, DateOnly End) PreviousWeek(DateOnly today)
    {
        int daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateOnly start = today.AddDays(-daysSinceMonday).AddDays(-7);
        return (start, start.AddDays(6));
    }

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
            database.ScheduledDispatches.Add(new ScheduledDispatchRow { DispatchKey = key, SentAt = DateTimeOffset.UtcNow, MessageId = messageId });
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
