namespace Aegis.Cafeteria.Infrastructure.Logging;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;
using Microsoft.Extensions.Logging;

public sealed class LoggingCafeteriaPersistenceStore(ICafeteriaPersistenceStore inner, ILogger<LoggingCafeteriaPersistenceStore> logger) : ICafeteriaPersistenceStore
{
    public async Task SaveCustomerAsync(Customer x,CancellationToken ct=default)
    { await inner.SaveCustomerAsync(x,ct); logger.LogInformation("Customer {CustomerId} persisted; eligible for credit {IsEligibleForCredit}",x.Id,x.IsEligibleForCredit); }
    public async Task SaveCustomerIdentifierAsync(CustomerIdentifier x,CancellationToken ct=default)
    { await inner.SaveCustomerIdentifierAsync(x,ct); logger.LogInformation("Customer identifier {IdentifierId} for customer {CustomerId} persisted; type {IdentifierType}",x.Id,x.CustomerId,x.Type); }
    public async Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocation x,CancellationToken ct=default)
    { await inner.SaveMonthlyCreditAllocationAsync(x,ct); logger.LogInformation("Monthly credit allocation for customer {CustomerId} {Year}-{Month}; days {Days}; employee-funded {EmployeeFundedAllocation}; subsidy {SubsidyAllocation}; total {TotalCredit}",x.CustomerId,x.Year,x.Month,x.EligibleWorkingDays,x.EmployeeFundedAllocation,x.SubsidyAllocation,x.TotalCreditLimit); }
    public async Task SaveProductAsync(Product x,CancellationToken ct=default)
    { await inner.SaveProductAsync(x,ct); logger.LogInformation("Product {ProductId} persisted at price {Price}",x.Id,x.Price); }
    public async Task SaveOrderAsync(Order x,CancellationToken ct=default)
    { await inner.SaveOrderAsync(x,ct); logger.LogInformation("Order {OrderId} for customer {CustomerId} persisted; status {Status}; total {Total}; cafeteria {CafeteriaAmount}; cash {CashAmount}; refunds {RefundedAmount}",x.Id,x.CustomerId,x.Status,x.Total,x.CafeteriaAccountPaid,x.CashPaid,x.RefundedAmount); }
    public async Task SaveAdditionalCreditApprovalAsync(AdditionalCreditApproval x,CancellationToken ct=default)
    { await inner.SaveAdditionalCreditApprovalAsync(x,ct); logger.LogInformation("Additional credit {ApprovalId} for customer {CustomerId}: {Amount} approved by {ApprovedBy}",x.Id,x.CustomerId,x.Amount,x.ApprovedBy); }
    public async Task SaveCustomerPaymentAsync(CustomerPayment x,CancellationToken ct=default)
    { await inner.SaveCustomerPaymentAsync(x,ct); logger.LogInformation("Customer payment {PaymentId} for customer {CustomerId}: {Amount}",x.Id,x.CustomerId,x.Amount); }
    public async Task SaveCarriedForwardDebtAsync(CarriedForwardDebt x,CancellationToken ct=default)
    { await inner.SaveCarriedForwardDebtAsync(x,ct); logger.LogWarning("Debt {DebtId} carried forward for customer {CustomerId}: {Amount}; source {SourceYear}-{SourceMonth}; applied {Year}-{Month}",x.Id,x.CustomerId,x.Amount,x.SourceYear,x.SourceMonth,x.AppliedYear,x.AppliedMonth); }
    public async Task SavePriorPeriodOutstandingSettlementAsync(PriorPeriodOutstandingSettlement x,CancellationToken ct=default)
    { await inner.SavePriorPeriodOutstandingSettlementAsync(x,ct); logger.LogWarning("Prior-period correction {SettlementId} for customer {CustomerId}: {Amount}; source {SourceYear}-{SourceMonth}; applied {Year}-{Month}",x.Id,x.CustomerId,x.Amount,x.SourceYear,x.SourceMonth,x.SettlementYear,x.SettlementMonth); }
    public async Task SavePriorPeriodRefundCreditAsync(PriorPeriodRefundCredit x,CancellationToken ct=default)
    { await inner.SavePriorPeriodRefundCreditAsync(x,ct); logger.LogInformation("Prior-period refund credit {CreditId} for customer {CustomerId}: {Amount}; source order {OrderId}",x.Id,x.CustomerId,x.CafeteriaAccountAmount,x.SourceOrderId); }
    public async Task SaveStipendAsync(Stipend x,CancellationToken ct=default)
    { await inner.SaveStipendAsync(x,ct); logger.LogInformation("Off-site stipend {StipendId} for customer {CustomerId}; mode {Mode}; cash {CashAmount}; card reduction {CardReduction}; processed by {ProcessedBy}",x.Id,x.CustomerId,x.PaymentMode,x.CashAmount,x.CardReduction,x.ProcessedByUserId); }
    public async Task SaveAccountPeriodCloseAsync(AccountPeriodClose x,CancellationToken ct=default)
    { await inner.SaveAccountPeriodCloseAsync(x,ct); logger.LogInformation("Account period closed for customer {CustomerId}, {Year}-{Month}; employee amount due {Amount}; closed by {ClosedBy}; close {CloseId}",x.CustomerId,x.Period.Year,x.Period.Month,x.EmployeeAmountDue,x.ClosedBy,x.Id); }
}
