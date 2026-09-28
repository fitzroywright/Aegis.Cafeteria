namespace Aegis.Cafeteria.PersistenceTests;

using System.Text.Json.Nodes;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Infrastructure.Persistence;

public sealed class PersistencePayloadSerializerTests
{
    [Theory]
    [InlineData(2026, 9, false)]
    [InlineData(2025, 12, true)]
    public void Stipend_RoundTrip_PreservesFundingAndPaymentDetails(int year, int month, bool priorPeriod)
    {
        Stipend original = CreateStipend(year, month);

        Stipend restored = PersistencePayloadSerializer.Deserialize<Stipend>(PersistencePayloadSerializer.Serialize(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.CustomerId, restored.CustomerId);
        Assert.Equal(original.CashAmount, restored.CashAmount);
        Assert.Equal(original.CardReduction, restored.CardReduction);
        Assert.Equal(original.PaymentMode, restored.PaymentMode);
        Assert.Equal(original.ProcessedByUserId, restored.ProcessedByUserId);
        Assert.Equal(original.ProcessedAt, restored.ProcessedAt);
        Assert.Equal(original.ExpenseReportReference, restored.ExpenseReportReference);
        Assert.Equal(original.Notes, restored.Notes);
        Assert.Equal(year, restored.FundingYear);
        Assert.Equal(month, restored.FundingMonth);
        Assert.Equal(priorPeriod, restored.IsPriorPeriodAdjustment);
    }

    [Fact]
    public void Stipend_LegacyPayload_UsesProcessedPeriod()
    {
        JsonObject payload = JsonNode.Parse(PersistencePayloadSerializer.Serialize(CreateStipend(2025, 12)))!.AsObject();
        payload.Remove("fundingYear");
        payload.Remove("fundingMonth");

        Stipend restored = PersistencePayloadSerializer.Deserialize<Stipend>(payload.ToJsonString());

        Assert.Equal(2026, restored.FundingYear);
        Assert.Equal(9, restored.FundingMonth);
        Assert.False(restored.IsPriorPeriodAdjustment);
    }

    [Theory]
    [InlineData("fundingYear", 1999)]
    [InlineData("fundingMonth", 0)]
    [InlineData("fundingMonth", 13)]
    public void Stipend_InvalidFundingPeriod_IsRejected(string property, int value)
    {
        JsonObject payload = JsonNode.Parse(PersistencePayloadSerializer.Serialize(CreateStipend(2026, 9)))!.AsObject();
        payload[property] = value;

        Assert.Throws<ArgumentOutOfRangeException>(() => PersistencePayloadSerializer.Deserialize<Stipend>(payload.ToJsonString()));
    }

    private static Stipend CreateStipend(int year, int month)
        => new(Guid.NewGuid(), 77, 600m, 400m, StipendPaymentMode.Reimbursement, "accounts",
            new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), "EXP-100", "Off-site meal", year, month);
}
