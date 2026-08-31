namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriodClose
{
    public AccountPeriodClose(
        Guid id,
        int customerId,
        AccountPeriod period,
        decimal employeeAmountDue,
        string closedBy,
        DateTimeOffset closedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Close ID is required.", nameof(id));
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        ArgumentNullException.ThrowIfNull(period);
        if (employeeAmountDue < 0) throw new ArgumentOutOfRangeException(nameof(employeeAmountDue));
        if (string.IsNullOrWhiteSpace(closedBy)) throw new ArgumentException("Closing user is required.", nameof(closedBy));

        Id = id;
        CustomerId = customerId;
        Period = period;
        EmployeeAmountDue = employeeAmountDue;
        ClosedBy = closedBy.Trim();
        ClosedAt = closedAt;
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public AccountPeriod Period { get; }
    public decimal EmployeeAmountDue { get; }
    public string ClosedBy { get; }
    public DateTimeOffset ClosedAt { get; }
}
