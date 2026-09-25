namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Identification;

public sealed class SharedPosBusinessPolicyTests
{
    [Fact]
    public void TenderQuote_CalculatesAccountCashChangeAndResponsibility()
    {
        TenderQuote quote = TenderQuoteCalculator.Calculate
        (
              total: 1000m
            , availableToSpend: 800m
            , subsidyRemaining: 500m
            , cafeteriaAccountAllowed: true
            , creditSalesAllowed: false
            , cafeteriaAccountAmount: 800m
            , cashReceived: 300m
            , useEmployeeCredit: false
        );

        Assert.Equal(200m, quote.CashApplied);
        Assert.Equal(100m, quote.ChangeAmount);
        Assert.Equal(300m, quote.NewCustomerResponsibility);
        Assert.True(quote.IsComplete);
    }

    [Fact]
    public void CustomerPaymentPolicy_RejectsClosedPeriod()
    {
        CustomerAccountView account = Account(isCash: false, eligible: true, amountDue: 500m, closed: true);
        CustomerPaymentEligibility result = CustomerPaymentPolicy.Evaluate(account);

        Assert.False(result.Allowed);
        Assert.Equal(0m, result.MaximumPayment);
    }

    [Fact]
    public void WeeklySummaryPolicy_DoesNotPromptCashCustomer()
    {
        CustomerAccountView account = Account(isCash: true, eligible: false, amountDue: 0m, closed: false);
        WeeklySummaryPromptDecision decision = WeeklySummaryPromptPolicy.Evaluate(account, false, "employee@example.com");

        Assert.False(decision.ShouldPrompt);
    }


    [Fact]
    public void TenderCustomerPolicy_DisablesAccountTenderForCashCustomer()
    {
        TenderCustomerContext context = TenderCustomerPolicy.Evaluate
        (
            Account(isCash: true, eligible: false, amountDue: 0m, closed: false)
        );

        Assert.False(context.CafeteriaAccountAllowed);
        Assert.False(context.CreditSalesAllowed);
        Assert.Equal(0m, context.AvailableToSpend);
    }

    [Fact]
    public void RefundEligibility_RejectsDraftOrder()
    {
        Order order = new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Lunch", 500m, 1));

        RefundEligibility result = RefundEligibilityPolicy.Evaluate(order);

        Assert.False(result.Allowed);
    }

    [Fact]
    public void RefundQuote_UsesCreditBeforeCashAfterAccount()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Order order = new(Guid.NewGuid(), 42, now);
        OrderItem item = new(1, "Lunch", 1000m, 1);
        order.AddItem(item);
        order.Submit(now);
        order.Complete
        (
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 500m, now),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Credit, 300m, now),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 200m, now)
            ],
            now
        );

        RefundQuote quote = RefundQuoteCalculator.Calculate(order, [(item.Id, 1)], 500m);

        Assert.Equal(500m, quote.CafeteriaAccountAmount);
        Assert.Equal(300m, quote.CreditAmount);
        Assert.Equal(200m, quote.CashAmount);
        Assert.True(quote.IsValid);
    }

    private static CustomerAccountView Account(bool isCash, bool eligible, decimal amountDue, bool closed)
        => new
        (
              new CustomerIdentityView(42, "Employee", null, eligible, IsCashCustomer: isCash)
            , new AccountPeriodView(2026, 9, 20, 0m, 0m, closed)
            , new CustomerFinancialStatusView
            (
                  CreditLimit: 1000m
                , SubsidyLimit: 500m
                , SubsidyUsed: 0m
                , SubsidyAmountRemaining: 500m
                , NonSubsidyAmountRemaining: 500m
                , TotalAvailableToSpend: 1000m
                , CarriedForwardDebt: 0m
                , GrossCustomerLiability: amountDue
                , TotalOrderSales: 0m
                , CafeteriaAccountSpending: 0m
                , CashSpending: 0m
                , AdditionalCreditApproved: 0m
                , AdditionalCreditUsed: 0m
                , AdditionalCreditRemaining: 0m
                , StipendCashPaid: 0m
                , StipendCardReduction: 0m
                , PriorPeriodSettlements: 0m
                , PriorPeriodRefundCredits: 0m
                , CustomerPayments: 0m
                , CustomerResponsibility: amountDue
                , EmployeeAmountDue: amountDue
                , TotalRefunds: 0m
                , CurrentCreditSaleDebt: 0m
            )
            , []
            , new ExternalPublicationStatus(false, 0, 0, null, null)
            , Identification: new CustomerIdentificationContext(IDMethod.Name, "TEST")
        );
}
