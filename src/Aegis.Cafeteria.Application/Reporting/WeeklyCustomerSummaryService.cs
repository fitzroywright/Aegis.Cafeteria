namespace Aegis.Cafeteria.Application.Reporting;

using Aegis.Cafeteria.Application.Messaging;
using Microsoft.Extensions.Logging;

public sealed class WeeklyCustomerSummaryService(
    IWeeklyTransactionSummaryQuery query,
    IWeeklySummaryDispatchStore dispatchStore,
    ICafeteriaMessagingGateway messaging,
    ILogger<WeeklyCustomerSummaryService> logger)
{
    public async Task<int> SendWeekAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken = default)
    {
        if (weekEnd < weekStart) throw new ArgumentException("Week end cannot precede week start.", nameof(weekEnd));
        var summaries = await query.GetAsync(weekStart, weekEnd, cancellationToken);
        var sent = 0;
        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (summary.TransactionCount == 0 || await dispatchStore.WasSentAsync(summary.CustomerId, weekStart, cancellationToken))
                continue;

            var messageId = DeterministicMessageId(summary.CustomerId, weekStart);
            var body = BuildText(summary);
            await messaging.SendAsync(new CafeteriaEmployeeMessage(
                messageId,
                summary.CustomerId,
                "Cafeteria.WeeklySummary",
                $"Cafeteria transactions: {weekStart:yyyy-MM-dd} to {weekEnd:yyyy-MM-dd}",
                body,
                DateTimeOffset.UtcNow), cancellationToken);
            await dispatchStore.MarkSentAsync(summary.CustomerId, weekStart, messageId, DateTimeOffset.UtcNow, cancellationToken);
            sent++;
        }
        logger.LogInformation("Weekly cafeteria summary complete for {WeekStart} through {WeekEnd}: {SentCount} messages sent", weekStart, weekEnd, sent);
        return sent;
    }

    public static string BuildText(WeeklyTransactionSummary x) =>
        $"Cafeteria weekly summary for {x.CustomerName}\n" +
        $"Period: {x.WeekStart:yyyy-MM-dd} to {x.WeekEnd:yyyy-MM-dd}\n" +
        $"Transactions: {x.TransactionCount}\n" +
        $"Total purchases: {x.TotalSales:C}\n" +
        $"Charged to cafeteria account: {x.CafeteriaAccountSpending:C}\n" +
        $"Cash paid: {x.CashSpending:C}\n" +
        $"Refunds: {x.Refunds:C}\n" +
        $"Available cafeteria balance: {x.EndingAvailableBalance:C}\n";

    private static Guid DeterministicMessageId(int customerId, DateOnly weekStart)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"cafeteria-weekly:{customerId}:{weekStart:yyyy-MM-dd}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
