namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

public sealed class ScheduledEmailWorker(
    ScheduledEmailOptions options,
    FfpEmailReportSource source,
    SmtpEmailSender sender,
    IDbContextFactory<ServiceDbContext> factory,
    ILogger<ScheduledEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now=DateTime.Now;
                var today=DateOnly.FromDateTime(now.Date);
                // Catch up yesterday if the service was unavailable at the normal daily delivery time.
                await SendDailyOnceAsync(today.AddDays(-1),stoppingToken);
                if(now.Hour>=options.DailySummaryHourLocal) await SendDailyOnceAsync(today,stoppingToken);
                var daysSinceMonday=((int)now.DayOfWeek-(int)DayOfWeek.Monday+7)%7;
                var deliveryOffset=((int)options.WeeklyEmployeeDay-(int)DayOfWeek.Monday+7)%7;
                if(daysSinceMonday>deliveryOffset || (daysSinceMonday==deliveryOffset && now.Hour>=options.WeeklyEmployeeHourLocal))
                    await SendPreviousWeekOnceAsync(today,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Scheduled Cafeteria email job failed; it will retry");}
            await Task.Delay(TimeSpan.FromMinutes(15),stoppingToken);
        }
    }

    private async Task SendDailyOnceAsync(DateOnly date,CancellationToken ct)
    {
        var key=$"daily:{date:yyyy-MM-dd}"; if(await WasSentAsync(key,ct)) return;
        var x=await source.GetDailySummaryAsync(date,ct);
        var body=$"Cafeteria Daily Summary - {date:dddd, MMMM d, yyyy}\n\nTransactions: {x.Transactions}\nPurchases: {x.Purchases:N2}\nRefunds: {x.Refunds:N2}\nAdditional Credit: {x.AdditionalCredit:N2}\nStipend Card Reductions: {x.StipendReductions:N2}\n";
        var id=await sender.SendAsync(options.DailySummaryRecipients,$"Cafeteria Daily Summary - {date:yyyy-MM-dd}",body,ct);
        if(!string.IsNullOrEmpty(id)) await MarkSentAsync(key,id,ct);
    }

    private async Task SendPreviousWeekOnceAsync(DateOnly today,CancellationToken ct)
    {
        var daysSinceMonday=((int)today.DayOfWeek-(int)DayOfWeek.Monday+7)%7;
        var thisMonday=today.AddDays(-daysSinceMonday); var start=thisMonday.AddDays(-7); var end=start.AddDays(6);
        var ledgers=await source.GetWeeklyEmployeeLedgersAsync(start,end,ct);
        foreach(var e in ledgers)
        {
            var key=$"weekly:{e.EmployeeId}:{start:yyyy-MM-dd}"; if(await WasSentAsync(key,ct)) continue;
            var sb=new StringBuilder(); sb.AppendLine($"Cafeteria transactions for {start:MMM d} - {end:MMM d, yyyy}").AppendLine();
            foreach(var line in e.Lines) sb.AppendLine($"{line.ScanDate:g} | {line.IdName} | {line.ScanValue:N2} | {line.Description}");
            var id=await sender.SendAsync([e.Email],$"Your Cafeteria Transactions - Week ending {end:yyyy-MM-dd}",sb.ToString(),ct);
            if(!string.IsNullOrEmpty(id)) await MarkSentAsync(key,id,ct);
        }
    }

    private async Task<bool> WasSentAsync(string key,CancellationToken ct){await using var db=await factory.CreateDbContextAsync(ct);return await db.ScheduledDispatches.AnyAsync(x=>x.DispatchKey==key,ct);}
    private async Task MarkSentAsync(string key,string id,CancellationToken ct){await using var db=await factory.CreateDbContextAsync(ct);if(!await db.ScheduledDispatches.AnyAsync(x=>x.DispatchKey==key,ct)){db.ScheduledDispatches.Add(new ScheduledDispatchRow{DispatchKey=key,SentAt=DateTimeOffset.UtcNow,MessageId=id});await db.SaveChangesAsync(ct);}}
}
