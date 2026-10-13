using Common.Diagnostics;

namespace Aegis.Cafeteria.Services.Acceptance;

public static class CafeteriaAcceptanceCatalogue
{
    public const string Version = "1";
    public static IReadOnlyList<AcceptanceProofDefinition> Required { get; } =
    [
        new("CAF-01", "FFP00 policy retrieval and interpretation", 2),
        new("CAF-02", "Employee fields and eligibility", 2),
        new("CAF-03", "Holidays and working days", 2),
        new("CAF-04", "Automatic credit limit", 2, "Monthly Credit Allocation"),
        new("CAF-05", "FFP00 subsidy limit", 2, "Monthly Credit Allocation"),
        new("CAF-06", "FFP credit and balance synchronization", 1, "Monthly Credit Allocation"),
        new("CAF-07", "ID scan publication", 2),
        new("CAF-08", "Services stipend deduction", 2, "Stipend Allocation"),
        new("CAF-09", "Services additional credit", 2, "Additional Credit"),
        new("CAF-10", "Monthly opening and initialization", 2, "Monthly Credit Allocation"),
        new("CAF-11", "Closing and rollover", 1),
        new("CAF-12", "Unused positive credit expiration", 1),
        new("CAF-13", "Outstanding debt carry-forward", 1),
        new("CAF-14", "Refunds, voids and prior-period credits", 1),
        new("CAF-15", "Payroll calculation and integration", 1),
        new("CAF-16", "Mid-month employee and eligibility changes", 1),
        new("CAF-17", "Cafeteria / FFP reconciliation", 1),
        new("CAF-18", "Idempotency and duplicate prevention", 2),
        new("CAF-19", "Interrupted publication and recovery", 1),
        new("CAF-20", "Complete financial workflow and manager bridge", 1)
    ];
}
