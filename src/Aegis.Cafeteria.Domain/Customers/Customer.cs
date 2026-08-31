namespace Aegis.Cafeteria.Domain.Customers;

public sealed class Customer
{
    public Customer(int id, string name, byte[]? photo, bool isEligibleForCredit, decimal additionalCreditLimit)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        if (additionalCreditLimit < 0) throw new ArgumentOutOfRangeException(nameof(additionalCreditLimit));
        Id=id; Name=name.Trim(); Photo=photo; IsEligibleForCredit=isEligibleForCredit; AdditionalCreditLimit=additionalCreditLimit;
    }
    public int Id { get; }
    public string Name { get; }
    public byte[]? Photo { get; }
    public bool IsEligibleForCredit { get; }
    public decimal AdditionalCreditLimit { get; }
}
