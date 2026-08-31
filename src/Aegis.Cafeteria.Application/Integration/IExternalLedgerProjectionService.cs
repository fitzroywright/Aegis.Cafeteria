namespace Aegis.Cafeteria.Application.Integration;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public interface IExternalLedgerProjectionService
{
    Task PublishMonthlyAllocationAsync(Customer customer, MonthlyCreditAllocation allocation, CancellationToken cancellationToken = default);
    Task PublishCompletedOrderAsync(CustomerAccountView accountBeforePurchase, Order order, CancellationToken cancellationToken = default);
    Task PublishRefundAsync(CustomerAccountView accountBeforeRefund, Order order, OrderRefund refund, CancellationToken cancellationToken = default);
    Task PublishAdjustmentAsync(int customerId, Guid operationId, ExternalLedgerEntryKind kind, DateTimeOffset occurredAt,
        string description, decimal signedAvailabilityChange, decimal creditLimit, decimal availableBalance,
        CancellationToken cancellationToken = default, string userName = "SYSTEM", string idName = "Cafeteria Adjustment", string scanCode = "ADJUSTMENT");
}
