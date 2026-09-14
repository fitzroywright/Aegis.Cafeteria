namespace Aegis.Cafeteria.Domain.Customers;

public sealed class Customer
{
    public Customer(
        int id,
        string name,
        byte[]? photo,
        bool isEligibleForCredit,
        decimal additionalCreditLimit,
        bool isActive = true,
        DateOnly? employmentStartDate = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        if (additionalCreditLimit < 0) throw new ArgumentOutOfRangeException(nameof(additionalCreditLimit));

        Id = id;
        Name = name.Trim();
        Photo = photo;
        IsActive = isActive;
        IsEligibleForCredit = isActive && isEligibleForCredit;
        AdditionalCreditLimit = additionalCreditLimit;
        EmploymentStartDate = employmentStartDate;
    }

    public int Id { get; }
    public string Name { get; }
    public byte[]? Photo { get; }
    public bool IsActive { get; }
    public bool IsEligibleForCredit { get; }
    public decimal AdditionalCreditLimit { get; }
    public DateOnly? EmploymentStartDate { get; }
}
