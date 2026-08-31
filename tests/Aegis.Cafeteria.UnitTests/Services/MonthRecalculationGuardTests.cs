namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Development;

public sealed class MonthRecalculationGuardTests
{
    [Fact]
    public async Task CheckAsync_AllowsMonthWithAllocationOnly()
    {
        DevelopmentFfpGateway gateway = new();
        DateTimeOffset occurredAt = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

        await gateway.PublishLedgerEntryAsync
        (
            new ExternalLedgerEntry
            (
                  Guid.NewGuid()
                , 900001
                , ExternalLedgerEntryKind.MonthlyAllocation
                , occurredAt
                , "Tickets Assigned"
                , 10000m
                , 10000m
                , 10000m
                , "ALLOCATION"
                , "SYSTEM"
                , "Tickets Assigned"
            )
        );

        MonthRecalculationGuardResult result = await gateway.CheckAsync(2026, 8);

        Assert.True(result.IsSafe);
        Assert.Equal(0, result.ActivityCount);
    }

    [Fact]
    public async Task CheckAsync_BlocksMonthWithFinancialActivity()
    {
        DevelopmentFfpGateway gateway = new();
        DateTimeOffset occurredAt = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

        await gateway.PublishLedgerEntryAsync
        (
            new ExternalLedgerEntry
            (
                  Guid.NewGuid()
                , 900001
                , ExternalLedgerEntryKind.Purchase
                , occurredAt
                , "Lunch x1 @ 650.00"
                , 650m
                , 10000m
                , 9350m
                , "PURCHASE"
                , "cashier"
                , "Cafeteria Purchase"
            )
        );

        MonthRecalculationGuardResult result = await gateway.CheckAsync(2026, 8);

        Assert.False(result.IsSafe);
        Assert.Equal(1, result.ActivityCount);
        Assert.NotNull(result.Reason);
    }
}
