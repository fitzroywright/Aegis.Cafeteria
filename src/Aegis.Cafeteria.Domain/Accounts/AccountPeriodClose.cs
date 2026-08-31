namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriodClose
{
    public AccountPeriodClose(
        Guid id,
        int customerId,
        AccountPeriod period,
        decimal payrollDeduction,
        string closedBy,
        DateTimeOffset closedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Close ID is required.", nameof(id));
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        ArgumentNullException.ThrowIfNull(period);
        if (payrollDeduction < 0) throw new ArgumentOutOfRangeException(nameof(payrollDeduction));
        if (string.IsNullOrWhiteSpace(closedBy)) throw new ArgumentException("Closing user is required.", nameof(closedBy));

        Id = id;
        CustomerId = customerId;
        Period = period;
        PayrollDeduction = payrollDeduction;
        ClosedBy = closedBy.Trim();
        ClosedAt = closedAt;
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public AccountPeriod Period { get; }
    public decimal PayrollDeduction { get; }
    public string ClosedBy { get; }
    public DateTimeOffset ClosedAt { get; }
}
