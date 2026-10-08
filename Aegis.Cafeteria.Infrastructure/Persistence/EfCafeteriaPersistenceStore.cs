namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Aegis.Cafeteria.Infrastructure.Services;

public sealed class EfCafeteriaPersistenceStore(LocalPersistenceStore store) : ICafeteriaPersistenceStore, IMonthlyCreditAllocationStore
{
    private static long Revision() => DateTimeOffset.UtcNow.UtcDateTime.Ticks;
    public Task SaveCustomerAsync(Customer x,CancellationToken ct=default) => store.SaveCustomerAsync(DomainPersistenceMapper.ToRow(x,Revision(),DateTimeOffset.UtcNow),ct);
    public Task SaveCustomerIdentifierAsync(CustomerIdentifier x,CancellationToken ct=default) => store.SaveCustomerIdentifierAsync(DomainPersistenceMapper.ToRow(x,Revision(),DateTimeOffset.UtcNow),ct);
    public Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocation x,CancellationToken ct=default) => store.SaveMonthlyCreditAllocationAsync(DomainPersistenceMapper.ToRow(x,Revision(),DateTimeOffset.UtcNow),ct);
    public Task SaveAsync(MonthlyCreditAllocation x,CancellationToken ct=default) => SaveMonthlyCreditAllocationAsync(x,ct);
    public Task SaveProductAsync(Product x,CancellationToken ct=default) => store.SaveProductAsync(DomainPersistenceMapper.ToRow(x,Revision(),DateTimeOffset.UtcNow),ct);

    public Task SaveOrderAsync(Order x,CancellationToken ct=default)
    {
        PosTransactionEvent transactionEvent = PosTransactionEventFactory.Create(x);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PosTransactionOutboxRow outbox = new()
        {
            EventId = transactionEvent.EventId,
            CorrelationId = transactionEvent.CorrelationId,
            Kind = transactionEvent.Kind,
            OrderId = transactionEvent.OrderId,
            CustomerId = transactionEvent.CustomerId,
            Actor = transactionEvent.Actor,
            TerminalId = transactionEvent.TerminalId,
            AccountYear = transactionEvent.AccountYear,
            AccountMonth = transactionEvent.AccountMonth,
            OccurredAt = transactionEvent.OccurredAt,
            PayloadJson = transactionEvent.PayloadJson,
            Reason = transactionEvent.Reason,
            CreatedAt = now,
            NextAttemptAt = now
        };

        return store.SaveOrderWithTransactionAsync(DomainPersistenceMapper.ToRow(x,Revision(),now), outbox, ct);
    }

    public Task SaveAdditionalCreditApprovalAsync(AdditionalCreditApproval x,CancellationToken ct=default) => SaveEvent(x.Id,"AdditionalCreditApproval",x.CustomerId,x.ApprovedAt.Year,x.ApprovedAt.Month,x.Amount,x.ApprovedAt,x,ct);
    public Task SaveCustomerPaymentAsync(CustomerPayment x,CancellationToken ct=default) => SaveEvent(x.Id,"CustomerPayment",x.CustomerId,x.ReceivedAt.Year,x.ReceivedAt.Month,x.Amount,x.ReceivedAt,x,ct);
    public Task SaveCarriedForwardDebtAsync(CarriedForwardDebt x,CancellationToken ct=default) => SaveEvent(x.Id,"CarriedForwardDebt",x.CustomerId,x.AppliedYear,x.AppliedMonth,x.Amount,x.RecordedAt,x,ct);
    public Task SavePriorPeriodOutstandingSettlementAsync(PriorPeriodOutstandingSettlement x,CancellationToken ct=default) => SaveEvent(x.Id,"PriorPeriodOutstandingSettlement",x.CustomerId,x.SettlementYear,x.SettlementMonth,x.Amount,x.ApprovedAt,x,ct);
    public Task SavePriorPeriodRefundCreditAsync(PriorPeriodRefundCredit x,CancellationToken ct=default) => SaveEvent(x.Id,"PriorPeriodRefundCredit",x.CustomerId,x.AppliedYear,x.AppliedMonth,x.CafeteriaAccountAmount,x.RecordedAt,x,ct);
    public Task SaveStipendAsync(Stipend x,CancellationToken ct=default) => SaveEvent(x.Id,"Stipend",x.CustomerId,x.ProcessedAt.Year,x.ProcessedAt.Month,x.Amount,x.ProcessedAt,x,ct);

    public async Task<MonthlyCreditAllocation?> GetAsync(int customerId, int year, int month, CancellationToken ct=default)
    {
        var row = await store.GetMonthlyCreditAllocationAsync(customerId, year, month, ct);
        if (row is null) return null;
        return new MonthlyCreditAllocation(row.CustomerId,row.Year,row.Month,row.EligibleWorkingDays,row.IsEligibleForCredit,row.CreditLimitPrice,row.SubsidyCost,row.CalculatedAt);
    }

    public async Task SaveAccountPeriodCloseAsync(AccountPeriodClose x,CancellationToken ct=default)
    {
        var row=new AccountPeriodRow{CustomerId=x.CustomerId,Year=x.Period.Year,Month=x.Period.Month,WorkingDays=x.Period.WorkingDays,IsClosed=true,ClosedAt=x.ClosedAt,ClosedBy=x.ClosedBy,FinalPayrollDeduction=x.EmployeeAmountDue,Revision=Revision(),UpdatedAt=DateTimeOffset.UtcNow};
        await store.SaveAccountPeriodAsync(row,ct);
    }

    private Task SaveEvent<T>(Guid id,string type,int customerId,int year,int month,decimal amount,DateTimeOffset at,T payload,CancellationToken ct)
        => store.SaveAccountEventAsync(new AccountEventRow{Id=id,EventType=type,CustomerId=customerId,AccountYear=year,AccountMonth=month,Amount=amount,PayloadJson=PersistencePayloadSerializer.Serialize(payload),OccurredAt=at,Revision=Revision(),UpdatedAt=DateTimeOffset.UtcNow},ct);
}
