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
    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly ILogger<ScheduledEmailWorker> logger;

    public ScheduledEmailWorker
    (
          ScheduledEmailOptions options
        , FfpEmailReportSource source
        , PayrollDeductionReportSource payrollSource
        , SmtpEmailSender sender
        , IDbContextFactory<ServiceDbContext> factory
        , ILogger<ScheduledEmailWorker> logger
    )
    {
        this.options = options;
        this.source = source;
        this.payrollSource = payrollSource;
        this.sender = sender;
        this.factory = factory;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!this.options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTime now = DateTime.Now;
                DateOnly today = DateOnly.FromDateTime(now.Date);

                await SendDailyOnceAsync(today.AddDays(-1), stoppingToken);

                if (now.Hour >= this.options.DailySummaryHourLocal)
                {
                    await SendDailyOnceAsync(today, stoppingToken);
                }

                int daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                int deliveryOffset = ((int)this.options.WeeklyEmployeeDay - (int)DayOfWeek.Monday + 7) % 7;

                if (daysSinceMonday > deliveryOffset ||
                    (daysSinceMonday == deliveryOffset && now.Hour >= this.options.WeeklyEmployeeHourLocal))
                {
                    await SendPreviousWeekOnceAsync(today, stoppingToken);
                }

                if (this.options.PayrollNotificationEnabled && now.Hour >= this.options.PayrollReportHourLocal)
                {
                    DateOnly previousMonth = today.AddDays(1 - today.Day).AddMonths(-1);
                    await SendPayrollReportAsync
                    (
                          previousMonth.Year
                        , previousMonth.Month
                        , false
                        , stoppingToken
                    );
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                this.logger.LogError(exception, "Scheduled Cafeteria email job failed; it will retry");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    public async Task<PayrollDeductionReportPackage> SendPayrollReportAsync
    (
          int year
        , int month
        , bool force
        , CancellationToken cancellationToken = default
    )
    {
        string key = $"payroll:{year:D4}-{month:D2}";
        PayrollDeductionReportPackage report = await this.payrollSource.BuildAsync(year, month, cancellationToken);

        if (!force && await WasSentAsync(key, cancellationToken))
        {
            return report;
        }

        if (this.options.PayrollRecipients.Length == 0)
        {
            this.logger.LogWarning
            (
                  "Payroll report for {Year}-{Month:D2} was not sent because ScheduledEmail:PayrollRecipients is empty"
                , year
                , month
            );

            return report;
        }

        string periodName = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        string subject = $"Cafeteria Employee Amount Due - {periodName}";
        StringBuilder body = new();
        body.AppendLine($"Cafeteria employee deduction notification for {periodName}").AppendLine();
        body.AppendLine($"Employees requiring deduction: {report.EmployeesWithAmountDue:N0}");
        body.AppendLine($"Total Employee Amount Due: {report.TotalEmployeeAmountDue:N2}").AppendLine();
        body.AppendLine("The attached Detail report lists each employee with a positive Cafeteria Employee Amount Due and the calculation components used to derive it.");
        body.AppendLine("The attached Summary report provides totals by department and an overall total.").AppendLine();
        body.AppendLine("Cafeteria reports the amount due only. Payroll remains responsible for deduction, collection and reconciliation.");

        EmailAttachment[] attachments =
        [
            new EmailAttachment
            (
                  $"Cafeteria-Payroll-Detail-{year:D4}-{month:D2}.csv"
                , "text/csv"
                , PayrollDeductionReportSource.BuildDetailCsv(report)
            )
          , new EmailAttachment
            (
                  $"Cafeteria-Payroll-Summary-{year:D4}-{month:D2}.csv"
                , "text/csv"
                , PayrollDeductionReportSource.BuildSummaryCsv(report)
            )
        ];

        string messageId = await this.sender.SendAsync
        (
              this.options.PayrollRecipients
            , subject
            , body.ToString()
            , attachments
            , cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(messageId))
        {
            await MarkSentAsync(key, messageId, cancellationToken);
        }

        return report;
    }

    private async Task SendDailyOnceAsync(DateOnly date, CancellationToken cancellationToken)
    {
        string key = $"daily:{date:yyyy-MM-dd}";

        if (await WasSentAsync(key, cancellationToken))
        {
            return;
        }

        DailyLedgerSummary summary = await this.source.GetDailySummaryAsync(date, cancellationToken);
        string body = $"Cafeteria Daily Summary - {date:dddd, MMMM d, yyyy}\n\nTransactions: {summary.Transactions}\nPurchases: {summary.Purchases:N2}\nRefunds: {summary.Refunds:N2}\nAdditional Credit: {summary.AdditionalCredit:N2}\nStipend Card Reductions: {summary.StipendReductions:N2}\n";
        string messageId = await this.sender.SendAsync
        (
              this.options.DailySummaryRecipients
            , $"Cafeteria Daily Summary - {date:yyyy-MM-dd}"
            , body
            , cancellationToken
        );

        if (!string.IsNullOrWhiteSpace(messageId))
        {
            await MarkSentAsync(key, messageId, cancellationToken);
        }
    }

    private async Task SendPreviousWeekOnceAsync(DateOnly today, CancellationToken cancellationToken)
    {
        int daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateOnly thisMonday = today.AddDays(-daysSinceMonday);
        DateOnly start = thisMonday.AddDays(-7);
        DateOnly end = start.AddDays(6);
        IReadOnlyList<EmployeeWeeklyLedger> ledgers = await this.source.GetWeeklyEmployeeLedgersAsync(start, end, cancellationToken);

        foreach (EmployeeWeeklyLedger employee in ledgers)
        {
            string key = $"weekly:{employee.EmployeeId}:{start:yyyy-MM-dd}";

            if (await WasSentAsync(key, cancellationToken))
            {
                continue;
            }

            StringBuilder body = new();
            body.AppendLine($"Cafeteria transactions for {start:MMM d} - {end:MMM d, yyyy}").AppendLine();

            foreach (EmployeeWeeklyLedgerLine line in employee.Lines)
            {
                body.AppendLine($"{line.ScanDate:g} | {line.IdName} | {line.ScanValue:N2} | {line.Description}");
            }

            string messageId = await this.sender.SendAsync
            (
                  [employee.Email]
                , $"Your Cafeteria Transactions - Week ending {end:yyyy-MM-dd}"
                , body.ToString()
                , cancellationToken
            );

            if (!string.IsNullOrWhiteSpace(messageId))
            {
                await MarkSentAsync(key, messageId, cancellationToken);
            }
        }
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
            database.ScheduledDispatches.Add
            (
                new ScheduledDispatchRow
                {
                      DispatchKey = key
                    , SentAt = DateTimeOffset.UtcNow
                    , MessageId = messageId
                }
            );

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
