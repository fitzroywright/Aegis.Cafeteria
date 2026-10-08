namespace Aegis.Cafeteria.PersistenceTests;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Infrastructure.Persistence;
public sealed class PersistencePayloadSerializerTests
{
    [Fact]
    public void Stipend_RoundTrip_PreservesExactDebit()
    {
        var original = new Stipend(Guid.NewGuid(), 77, 5000m, "accounts",
            new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero), "Approved deduction");
        Stipend restored = PersistencePayloadSerializer.Deserialize<Stipend>(PersistencePayloadSerializer.Serialize(original));
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.CustomerId, restored.CustomerId);
        Assert.Equal(original.Amount, restored.Amount);
        Assert.Equal(original.ProcessedByUserId, restored.ProcessedByUserId);
        Assert.Equal(original.ProcessedAt, restored.ProcessedAt);
        Assert.Equal(original.Reason, restored.Reason);
    }
}
