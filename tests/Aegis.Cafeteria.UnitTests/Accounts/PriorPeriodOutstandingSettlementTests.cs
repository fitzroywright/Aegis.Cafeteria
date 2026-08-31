namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class PriorPeriodOutstandingSettlementTests
{
    [Fact]
    public void Constructor_PreservesAuditInformation()
    {
        var approvedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        var settlement = new PriorPeriodOutstandingSettlement(
            Guid.NewGuid(),
            10,
            2026,
            8,
            2026,
            9,
            2_500m,
            "manager",
            approvedAt,
            "August employee amount due omitted.");

        Assert.Equal(2026, settlement.SourceYear);
        Assert.Equal(8, settlement.SourceMonth);
        Assert.Equal(2026, settlement.SettlementYear);
        Assert.Equal(9, settlement.SettlementMonth);
        Assert.Equal(2_500m, settlement.Amount);
        Assert.Equal("manager", settlement.ApprovedBy);
        Assert.Equal("August employee amount due omitted.", settlement.Reason);
    }

    [Fact]
    public void Constructor_WhenReasonMissing_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PriorPeriodOutstandingSettlement(
                Guid.NewGuid(), 10, 2026, 8, 2026, 9, 500m,
                "manager", DateTimeOffset.UtcNow, " "));
    }

    [Fact]
    public void Constructor_WhenSourcePeriodIsNotEarlier_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new PriorPeriodOutstandingSettlement(
                Guid.NewGuid(), 10, 2026, 9, 2026, 9, 500m,
                "manager", DateTimeOffset.UtcNow, "Correction"));
    }
}
