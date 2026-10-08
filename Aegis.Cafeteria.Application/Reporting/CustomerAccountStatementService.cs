namespace Aegis.Cafeteria.Application.Reporting;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Reporting;

public sealed class CustomerAccountStatementService
{
    public CustomerAccountStatement Build(
        Customer customer,
        AccountPeriod period,
        CustomerAccountSnapshot snapshot,
        IEnumerable<Order> orders,
        IEnumerable<AdditionalCreditApproval> approvals,
        IEnumerable<CustomerPayment> payments,
        IEnumerable<PriorPeriodOutstandingSettlement> settlements,
        IEnumerable<Stipend> stipends,
        IEnumerable<PriorPeriodRefundCredit> priorPeriodRefundCredits,
        AccountPeriodClose? close = null)
    {
        var entries = new List<CustomerAccountStatementEntry>();

        foreach (var order in orders.Where(x => x.CustomerId == customer.Id && x.CompletedAt.HasValue && period.Contains(x.CompletedAt.Value)))
        {
            if (order.CafeteriaAccountPaid > 0)
                entries.Add(new(order.CompletedAt!.Value, "Purchase", "Cafeteria account purchase", order.CafeteriaAccountPaid, order.Id));
            if (order.CashPaid > 0)
                entries.Add(new(order.CompletedAt!.Value, "Cash", "Cash tender", order.CashPaid, order.Id));
            foreach (var refund in order.Refunds.Where(x => period.Contains(x.RefundedAt)))
                entries.Add(new(refund.RefundedAt, "Refund", refund.Reason, -refund.Total, refund.Id));
        }

        entries.AddRange(approvals.Where(x => x.CustomerId == customer.Id && period.Contains(x.ApprovedAt))
            .Select(x => new CustomerAccountStatementEntry(x.ApprovedAt, "Additional Credit", "Manager-approved additional credit", x.Amount, x.Id)));
        entries.AddRange(payments.Where(x => x.CustomerId == customer.Id && period.Contains(x.ReceivedAt))
            .Select(x => new CustomerAccountStatementEntry(x.ReceivedAt, "Payment", "Customer payment", -x.Amount, x.Id)));
        entries.AddRange(settlements.Where(x => x.CustomerId == customer.Id && x.SettlementYear == period.Year && x.SettlementMonth == period.Month)
            .Select(x => new CustomerAccountStatementEntry(x.ApprovedAt, "Prior Period Settlement", x.Reason, x.Amount, x.Id)));
        entries.AddRange(stipends.Where(x => x.CustomerId == customer.Id && period.Contains(x.ProcessedAt))
            .Select(x => new CustomerAccountStatementEntry(x.ProcessedAt, "Stipend", x.Reason ?? "Cafeteria stipend deduction", x.Amount, x.Id)));
        entries.AddRange(priorPeriodRefundCredits.Where(x => x.CustomerId == customer.Id && x.AppliedYear == period.Year && x.AppliedMonth == period.Month)
            .Select(x => new CustomerAccountStatementEntry(x.RecordedAt, "Prior Period Refund Credit", x.Reason, -x.CafeteriaAccountAmount, x.Id)));

        return new CustomerAccountStatement(customer, period, snapshot, entries.OrderBy(x => x.OccurredAt).ToArray(), close is not null, close);
    }
}
