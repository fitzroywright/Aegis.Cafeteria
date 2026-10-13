namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Services;
using System.Text.Json;
using Xunit;

public sealed class AuditCalculationDetailsTests
{
    [Fact]
    public void MonthlyAllocation_DisplaysRecordedInputsAndSubsidyCap()
    {
        var value = Allocation(900m, 1200m);
        string json = JsonSerializer.Serialize(new
        {
            id = 1, occurredAt = value.CalculatedAt, actor = "SYSTEM", action = "MonthlyAllocation",
            entityType = "CafeteriaServiceEvent", entityId = "1", customerId = 7, source = "ServiceEventStore",
            succeeded = true, statusCode = (int?)null, detail = "Created",
            afterJson = JsonSerializer.Serialize(value), operationId = value.OperationId
        });
        var audit = JsonSerializer.Deserialize<AuditTrailSnapshot>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(audit);
        Assert.Equal(value.OperationId, audit.OperationId);
        string text = AuditCalculationDetails.Format(audit);
        Assert.Contains("21 × J$900.00 = J$18,900.00", text);
        Assert.Contains("min(J$18,900.00, 21 × J$1,200.00) = J$18,900.00", text);
        Assert.Contains("Employee-funded portion: J$18,900.00 − J$18,900.00 = J$0.00", text);
    }

    [Fact]
    public void Recalculation_ShowsBeforeAfterAndChanges()
    {
        var previous = Allocation(0m, 0m);
        var replacement = Allocation(900m, 600m);
        var change = new MonthlyAllocationRecalculationServiceEvent(Guid.NewGuid(), previous, replacement, "manager", DateTimeOffset.UtcNow);
        string text = AuditCalculationDetails.Format(Audit("MonthlyAllocationRecalculation", JsonSerializer.Serialize(change)));
        Assert.Contains("PREVIOUS ALLOCATION", text);
        Assert.Contains("REPLACEMENT ALLOCATION", text);
        Assert.Contains("Credit change: J$18,900.00", text);
        Assert.Contains("Subsidy change: J$12,600.00", text);
        Assert.Contains("Employee-funded portion: J$18,900.00 − J$12,600.00 = J$6,300.00", text);
        Assert.Contains("Recalculated by: manager", text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPayload_IsReportedWithoutInventingAmounts(string? payload)
    {
        string text = AuditCalculationDetails.Format(Audit("MonthlyAllocation", payload));
        Assert.Contains("Calculation payload is unavailable", text);
        Assert.DoesNotContain("J$", text);
    }

    [Fact]
    public void InvalidPayload_DoesNotCrashDetailView()
    {
        string text = AuditCalculationDetails.Format(Audit("MonthlyAllocationRecalculation", "{broken"));
        Assert.Contains("could not be interpreted", text);
        Assert.Contains("{broken", text);
    }

    [Fact]
    public void IneligibleEmployee_DisplaysZeroEntitlement()
    {
        var value = Allocation(900m, 600m) with { IsEligibleForCredit = false };
        string text = AuditCalculationDetails.Format(Audit("MonthlyAllocation", JsonSerializer.Serialize(value)));
        Assert.Contains("Eligible for credit: No", text);
        Assert.Contains("Monthly credit: J$0.00 (employee was not eligible)", text);
    }

    [Fact]
    public void LegacyAuditResponseWithoutPayload_RemainsCompatible()
    {
        string json = """{"id":1,"occurredAt":"2026-10-01T00:00:00+00:00","actor":"SYSTEM","action":"MonthlyAllocation","entityType":"CafeteriaServiceEvent","source":"ServiceEventStore","succeeded":true}""";
        var audit = JsonSerializer.Deserialize<AuditTrailSnapshot>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(audit);
        Assert.Null(audit.AfterJson);
        Assert.Contains("unavailable", AuditCalculationDetails.Format(audit));
    }

    private static MonthlyAllocationServiceEvent Allocation(decimal credit, decimal subsidy) =>
        new(Guid.NewGuid(), 7, 2026, 10, 21, true, credit, subsidy, DateTimeOffset.UtcNow, "Test Employee", "ManualMonthOpening");

    private static AuditTrailSnapshot Audit(string action, string? payload) =>
        new(1, DateTimeOffset.UtcNow, "SYSTEM", action, "CafeteriaServiceEvent", "1", 7, "ServiceEventStore", true, null, null)
        { AfterJson = payload };
}
