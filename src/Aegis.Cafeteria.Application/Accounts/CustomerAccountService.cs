namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Reporting;

public sealed class CustomerAccountService(CustomerAccountCalculator calculator, CustomerAccountStatementService statementService)
{
    public CustomerAccountStatement GetStatement(
        Customer customer,
        AccountPeriod period,
        MonthlyCreditAllocation allocation,
        IEnumerable<Order> orders,
        IEnumerable<AdditionalCreditApproval> approvals,
        IEnumerable<CustomerPayment> payments,
        IEnumerable<CarriedForwardDebt> carriedForwardDebts,
        IEnumerable<PriorPeriodOutstandingSettlement> settlements,
        IEnumerable<Stipend> stipends,
        IEnumerable<PriorPeriodRefundCredit> priorPeriodRefundCredits,
        AccountPeriodClose? close = null)
    {
        var snapshot = calculator.Calculate(
            customer: customer,
            period: period,
            allocation: allocation,
            orders: orders,
            additionalCreditApprovals: approvals,
            payments: payments,
            carriedForwardDebts: carriedForwardDebts,
            priorPeriodSettlements: settlements,
            stipends: stipends,
            priorPeriodRefundCredits: priorPeriodRefundCredits);
        return statementService.Build(customer, period, snapshot, orders, approvals, payments, settlements, stipends, priorPeriodRefundCredits, close);
    }

}
