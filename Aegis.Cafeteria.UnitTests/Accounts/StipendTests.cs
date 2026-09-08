namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class StipendTests
{
    [Theory]
    [InlineData(StipendPaymentMode.Advance)]
    [InlineData(StipendPaymentMode.Reimbursement)]
    public void Constructor_PreservesCashAndCardReduction(StipendPaymentMode mode)
    {
        var stipend = new Stipend(
            Guid.NewGuid(),
            customerId: 10,
            cashAmount: 800m,
            cardReduction: 650m,
            paymentMode: mode,
            processedByUserId: "accounts.user",
            processedAt: DateTimeOffset.UtcNow,
            expenseReportReference: "ER-1001");

        Assert.Equal(800m, stipend.CashAmount);
        Assert.Equal(650m, stipend.CardReduction);
        Assert.Equal(mode, stipend.PaymentMode);
        Assert.Equal("ER-1001", stipend.ExpenseReportReference);
    }

    [Fact]
    public void Constructor_CardReductionCannotExceedCashAmount()
    {
        Assert.Throws<ArgumentException>(() =>
            new Stipend(
                Guid.NewGuid(),
                10,
                800m,
                801m,
                StipendPaymentMode.Advance,
                "accounts.user",
                DateTimeOffset.UtcNow));
    }
}
