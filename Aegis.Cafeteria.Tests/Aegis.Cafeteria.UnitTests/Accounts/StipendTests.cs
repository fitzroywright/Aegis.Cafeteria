namespace Aegis.Cafeteria.UnitTests.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
public sealed class StipendTests
{
    [Fact]
    public void Constructor_PreservesExactDeduction()
    {
        Guid id = Guid.NewGuid();
        var stipend = new Stipend(id, 10, 5000m, "accounts.user", DateTimeOffset.UtcNow, "Approved deduction");
        Assert.Equal(id, stipend.Id);
        Assert.Equal(5000m, stipend.Amount);
        Assert.Equal("Approved deduction", stipend.Reason);
    }
    [Fact]
    public void Constructor_RejectsNonPositiveAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Stipend(Guid.NewGuid(), 10, 0m, "accounts.user", DateTimeOffset.UtcNow));
    }
}
