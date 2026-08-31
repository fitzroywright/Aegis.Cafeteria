namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;

public interface ICafeteriaPersistenceStore
{
    Task SaveCustomerAsync(Customer customer, CancellationToken ct = default);
    Task SaveCustomerIdentifierAsync(CustomerIdentifier identifier, CancellationToken ct = default);
    Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocation allocation, CancellationToken ct = default);
    Task SaveProductAsync(Product product, CancellationToken ct = default);
    Task SaveOrderAsync(Order order, CancellationToken ct = default);
    Task SaveAdditionalCreditApprovalAsync(AdditionalCreditApproval approval, CancellationToken ct = default);
    Task SaveCustomerPaymentAsync(CustomerPayment payment, CancellationToken ct = default);
    Task SaveCarriedForwardDebtAsync(CarriedForwardDebt debt, CancellationToken ct = default);
    Task SavePriorPeriodOutstandingSettlementAsync(PriorPeriodOutstandingSettlement settlement, CancellationToken ct = default);
    Task SavePriorPeriodRefundCreditAsync(PriorPeriodRefundCredit credit, CancellationToken ct = default);
    Task SaveStipendAsync(Stipend stipend, CancellationToken ct = default);
    Task SaveAccountPeriodCloseAsync(AccountPeriodClose close, CancellationToken ct = default);
}
