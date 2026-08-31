namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class MonthlyCreditAllocationTests
{
    [Fact]
    public void EligibleCustomer_CreditUsesFfp02AndSubsidyUsesFfp00()
    {
        var x = new MonthlyCreditAllocation(1, 2026, 8, 20, true, 800m, 600m, DateTimeOffset.UtcNow);
        Assert.Equal(4_000m, x.EmployeeFundedAllocation);
        Assert.Equal(12_000m, x.SubsidyAllocation);
        Assert.Equal(16_000m, x.TotalCreditLimit);
    }

    [Fact]
    public void IneligibleCustomer_GetsZeroAllocation()
    {
        var x = new MonthlyCreditAllocation(1, 2026, 8, 20, false, 800m, 600m, DateTimeOffset.UtcNow);
        Assert.Equal(0m, x.EmployeeFundedAllocation);
        Assert.Equal(0m, x.SubsidyAllocation);
        Assert.Equal(0m, x.TotalCreditLimit);
    }
}
