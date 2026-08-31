namespace Aegis.Cafeteria.Domain.Accounts;

public sealed record PayrollDeductionRecord(
    int CustomerId,
    int Year,
    int Month,
    decimal Amount,
    Guid PeriodCloseId);
