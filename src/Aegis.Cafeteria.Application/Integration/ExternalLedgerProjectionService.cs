namespace Aegis.Cafeteria.Application.Integration;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class ExternalLedgerProjectionService(IExternalLedgerOutbox outbox) : IExternalLedgerProjectionService
{
    public Task PublishMonthlyAllocationAsync(Customer customer, MonthlyCreditAllocation allocation, CancellationToken cancellationToken = default)
    {
        var description = $"{allocation.EligibleWorkingDays} new Tickets Assigned to {customer.Name} By SYSTEM";
        var entry = new ExternalLedgerEntry(
            OperationId: DeterministicOperationId(customer.Id, allocation.Year, allocation.Month, "monthly-allocation"),
            CustomerId: customer.Id,
            Kind: ExternalLedgerEntryKind.MonthlyAllocation,
            OccurredAt: allocation.CalculatedAt,
            Description: description,
            ScanValue: allocation.TotalCreditLimit,
            CreditLimit: allocation.TotalCreditLimit,
            AvailableBalance: allocation.TotalCreditLimit,
            ScanCode: "ALLOCATION",
            UserName: "SYSTEM",
            IdName: "Tickets Assigned");
        return outbox.EnqueueAsync(entry, cancellationToken);
    }

    public Task PublishCompletedOrderAsync(CustomerAccountView accountBeforePurchase, Order order, CancellationToken cancellationToken = default)
    {
        var accountAmount = order.CafeteriaAccountPaid;
        if (accountAmount <= 0m) return Task.CompletedTask;
        var postBalance = Math.Max(0m, accountBeforePurchase.FinancialStatus.TotalAvailableToSpend - accountAmount);
        var description = BuildPurchaseDescription(order);
        var entry = new ExternalLedgerEntry(
            OperationId: order.Id,
            CustomerId: accountBeforePurchase.Customer.Id,
            Kind: ExternalLedgerEntryKind.Purchase,
            OccurredAt: order.CompletedAt ?? DateTimeOffset.Now,
            Description: description,
            ScanValue: accountAmount,
            CreditLimit: accountBeforePurchase.FinancialStatus.TotalCreditLimit,
            AvailableBalance: postBalance,
            ScanCode: order.IDMethod?.ToString() ?? "Unknown",
            UserName: NormalizeUserName(order.CashierName ?? order.CashierUserId),
            IdName: "Cafeteria Purchase",
            Code: order.IdentificationCode,
            CreatedBy: "SYSTEM");
        return outbox.EnqueueAsync(entry, cancellationToken);
    }

    public Task PublishRefundAsync(CustomerAccountView accountBeforeRefund, Order order, OrderRefund refund, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (refund.CafeteriaAccountAmount <= 0m) return Task.CompletedTask;
        var postBalance = Math.Min(
            accountBeforeRefund.FinancialStatus.TotalCreditLimit,
            accountBeforeRefund.FinancialStatus.TotalAvailableToSpend + refund.CafeteriaAccountAmount);
        bool priorPeriodRefund = order.CompletedAt.HasValue &&
            (order.CompletedAt.Value.Year != refund.RefundedAt.Year || order.CompletedAt.Value.Month != refund.RefundedAt.Month);
        var description = BuildRefundDescription(order, refund);
        var entry = new ExternalLedgerEntry(
            refund.Id,
            accountBeforeRefund.Customer.Id,
            ExternalLedgerEntryKind.Refund,
            refund.RefundedAt,
            description,
            -refund.CafeteriaAccountAmount,
            accountBeforeRefund.FinancialStatus.TotalCreditLimit,
            postBalance,
            "REFUND",
            NormalizeUserName(refund.RefundedBy),
            priorPeriodRefund ? "Cafeteria Prior Period Refund" : "Cafeteria Refund");
        return outbox.EnqueueAsync(entry, cancellationToken);
    }

    public Task PublishAdjustmentAsync(int customerId, Guid operationId, ExternalLedgerEntryKind kind, DateTimeOffset occurredAt,
        string description, decimal signedAvailabilityChange, decimal creditLimit, decimal availableBalance,
        CancellationToken cancellationToken = default, string userName = "SYSTEM", string idName = "Cafeteria Adjustment", string scanCode = "ADJUSTMENT")
        => outbox.EnqueueAsync(new ExternalLedgerEntry(operationId, customerId, kind, occurredAt, description,
            signedAvailabilityChange, creditLimit, availableBalance, scanCode, NormalizeUserName(userName), idName), cancellationToken);

    private static string BuildPurchaseDescription(Order order)
    {
        var items = string.Join("; ", order.Items.Select(x => $"{x.ProductName} x{x.Quantity} @ {x.UnitPrice:N2}"));
        var tender = order.CafeteriaAccountPaid == order.Total
            ? $"Total: {order.Total:N2}"
            : $"Order Total: {order.Total:N2}; Cafeteria: {order.CafeteriaAccountPaid:N2}";
        return $"{items} - {tender}";
    }

    private static string BuildRefundDescription(Order order, OrderRefund refund)
    {
        var names = order.Items.ToDictionary(x => x.Id, x => x.ProductName);
        var items = string.Join("; ", refund.Items.Select(x => $"{names.GetValueOrDefault(x.OrderItemId, "Item")} x{x.Quantity} @ {x.UnitPrice:N2}"));
        var originalSale = order.CompletedAt.HasValue ? $"; Original Sale: {order.CompletedAt.Value:yyyy-MM-dd}" : string.Empty;
        return $"Refund - {items} - Cafeteria Refund: {refund.CafeteriaAccountAmount:N2}{originalSale}";
    }

    private static string NormalizeUserName(string? value)
        => string.IsNullOrWhiteSpace(value) ? "SYSTEM" : value.Trim();

    private static Guid DeterministicOperationId(int customerId, int year, int month, string purpose)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{customerId}:{year:D4}-{month:D2}:{purpose}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
