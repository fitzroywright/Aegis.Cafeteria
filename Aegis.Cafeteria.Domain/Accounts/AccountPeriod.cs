namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriod
{
    public AccountPeriod(int year, int month, int workingDays)
    {
        if (year < 1)
            throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month));
        if (workingDays < 0)
            throw new ArgumentOutOfRangeException(nameof(workingDays));

        Year = year;
        Month = month;
        WorkingDays = workingDays;
    }

    public int Year { get; }
    public int Month { get; }
    public int WorkingDays { get; }
    public DateOnly StartDate => new(Year, Month, 1);
    public DateOnly EndDate => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public bool Contains(DateTimeOffset instant)
        => instant.Year == Year && instant.Month == Month;
}
