namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;

public sealed class TenderCustomerPolicyTests
{
    [Fact]
    public void Evaluate_CreditShortfall_DisablesEmployeeCardButKeepsEmployeeCreditAvailable()
    {
        CustomerAccountView account = new(
            new CustomerIdentityView(1, "Employee", null, true, false),
            new AccountPeriodView(2026, 10, 0, 0m, 0m, false),
            new CustomerFinancialStatusView(
                CreditLimit: 0m,
                SubsidyLimit: 0m,
                SubsidyUsed: 0m,
                SubsidyAmountRemaining: 0m,
                NonSubsidyAmountRemaining: 0m,
                TotalAvailableToSpend: 0m,
                CarriedForwardDebt: 250m,
                GrossCustomerLiability: 250m,
                TotalOrderSales: 0m,
                CafeteriaAccountSpending: 0m,
                CashSpending: 0m,
                AdditionalCreditApproved: 0m,
                AdditionalCreditUsed: 0m,
                AdditionalCreditRemaining: 0m,
                StipendCashPaid: 0m,
                StipendCardReduction: 0m,
                PriorPeriodSettlements: 0m,
                PriorPeriodRefundCredits: 0m,
                CustomerPayments: 0m,
                CustomerResponsibility: 250m,
                EmployeeAmountDue: 250m,
                TotalRefunds: 0m,
                CurrentCreditSaleDebt: 0m,
                CreditShortfall: 250m),
            [],
            new ExternalPublicationStatus(true, 0, 0, DateTimeOffset.UtcNow, null));

        TenderCustomerContext result = TenderCustomerPolicy.Evaluate(account);

        Assert.False(result.CafeteriaAccountAllowed);
        Assert.True(result.CreditSalesAllowed);
        Assert.Equal(0m, result.AvailableToSpend);
        Assert.Equal(0m, result.SubsidyRemaining);
    }
}
