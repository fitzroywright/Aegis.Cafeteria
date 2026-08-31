namespace Aegis.Cafeteria.Domain.Identification;

public sealed record CustomerIdentifier
{
    public CustomerIdentifier(Guid id, int customerId, CustomerIdentifierType type, string value)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Identifier value is required.", nameof(value));
        Id = id;
        CustomerId = customerId;
        Type = type;
        Value = value.Trim();
    }
    public Guid Id { get; }
    public int CustomerId { get; }
    public CustomerIdentifierType Type { get; }
    public string Value { get; }
}
