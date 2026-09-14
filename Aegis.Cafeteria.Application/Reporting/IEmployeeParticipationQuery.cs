namespace Aegis.Cafeteria.Application.Reporting;

public interface IEmployeeParticipationQuery
{
    Task<EmployeeParticipationWeekView> GetWeekAsync
    (
        DateOnly monday,
        CancellationToken cancellationToken = default
    );
}

public sealed record EmployeeParticipationWeekView
(
    int EligibleEmployeeCount,
    IReadOnlyList<EmployeeParticipationDayView> Days
);

public sealed record EmployeeParticipationDayView
(
    DateOnly Date,
    int EmployeesServed
);
