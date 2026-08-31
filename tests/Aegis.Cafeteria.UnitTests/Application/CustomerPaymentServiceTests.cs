namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class CustomerPaymentServiceTests
{
    [Fact]
    public void RecordPayment_WhenWithinAmountDue_CreatesPayment()
    {
        var service = new CustomerPaymentService();
        var customer = CreateCustomer();
        var account = Snapshot(payrollDeductionRequired: 3_000m);

        var payment = service.RecordPayment(customer, account, 2_000m, DateTimeOffset.UtcNow, "Receipt 123");

        Assert.Equal(customer.Id, payment.CustomerId);
        Assert.Equal(2_000m, payment.Amount);
        Assert.Equal("Receipt 123", payment.Reference);
    }

    [Fact]
    public void RecordPayment_WhenGreaterThanAmountDue_Throws()
    {
        var service = new CustomerPaymentService();
        var customer = CreateCustomer();
        var account = Snapshot(payrollDeductionRequired: 3_000m);

        Assert.Throws<InvalidOperationException>(() =>
            service.RecordPayment(customer, account, 3_001m, DateTimeOffset.UtcNow));
    }

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

    private static CustomerAccountSnapshot Snapshot(decimal payrollDeductionRequired)
        => new(
            BaseMonthlyCredit: 0m,
            PriorPeriodOutstandingSettlements: 0m,
            StipendCashPaid: 0m,
            StipendCardReduction: 0m,
            BaseCreditAvailableForCurrentPurchases: 0m,
            AdditionalCreditApproved: 0m,
            TotalSpendingCapacity: 0m,
            TotalCharges: 0m,
            CompanyResponsibility: 0m,
            CompanyContributionUsed: 0m,
            RemainingCompanyContribution: 0m,
            CustomerResponsibilityForCurrentCharges: payrollDeductionRequired,
            CustomerPayments: 0m,
            PayrollDeductionRequired: payrollDeductionRequired,
            AvailableCredit: 0m,
            AdditionalCreditUsed: 0m,
            UnusedAdditionalCredit: 0m);
}
