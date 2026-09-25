namespace Aegis.Cafeteria.Application.Integration;

public enum SubsidyConsumptionMode
{
    Standard = 0,
    EmployerFirst = 1,
    Proportional = 2,
    EmployeeFirst = 3
}

public interface ICafeteriaPolicyProvider
{
    Task<CafeteriaPolicySnapshot> GetPolicyAsync(
        int year,
        int month,
        bool forceReload = false,
        CancellationToken cancellationToken = default);
}

public sealed record CafeteriaPolicySnapshot(
    int Year,
    int Month,
    decimal DailyCreditLimitRate,
    decimal DailyCompanySubsidyRate,
    IReadOnlyCollection<DateOnly> WorkingDays,
    string Version,
    DateTimeOffset EffectiveFrom,
    SubsidyConsumptionMode SubsidyConsumptionMode = SubsidyConsumptionMode.Standard)
{
    public void Validate()
    {
        if (Year < 2000 || Year > 9999 || Month is < 1 or > 12)
            throw new InvalidOperationException("Cafeteria policy period is invalid.");
        if (DailyCreditLimitRate <= 0m)
            throw new InvalidOperationException("Cafeteria policy credit-limit rate must be positive.");
        if (DailyCompanySubsidyRate < 0m || DailyCompanySubsidyRate > DailyCreditLimitRate)
            throw new InvalidOperationException("Cafeteria policy subsidy rate is invalid.");
        if (!Enum.IsDefined(typeof(SubsidyConsumptionMode), SubsidyConsumptionMode))
            throw new InvalidOperationException("Cafeteria policy subsidy consumption mode is invalid.");
        if (string.IsNullOrWhiteSpace(Version))
            throw new InvalidOperationException("Cafeteria policy version is required.");
        if (WorkingDays is null)
            throw new InvalidOperationException("Cafeteria policy working-day calendar is required.");

        DateOnly start = new(Year, Month, 1);
        DateOnly end = new(Year, Month, DateTime.DaysInMonth(Year, Month));
        if (WorkingDays.Any(date => date < start || date > end))
            throw new InvalidOperationException("Cafeteria policy contains a working day outside the requested period.");
    }
}
