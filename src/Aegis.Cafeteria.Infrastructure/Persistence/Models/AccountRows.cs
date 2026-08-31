namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class AccountEventRow
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int AccountYear { get; set; }
    public int AccountMonth { get; set; }
    public decimal Amount { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AccountPeriodRow
{
    public int CustomerId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int WorkingDays { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosedBy { get; set; }
    public decimal? FinalPayrollDeduction { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MonthlyCreditAllocationRow
{
    public int CustomerId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int EligibleWorkingDays { get; set; }
    public bool IsEligibleForCredit { get; set; }
    public decimal CreditLimitPrice { get; set; }
    public decimal SubsidyCost { get; set; }
    public decimal EmployeeFundedAllocation { get; set; }
    public decimal SubsidyAllocation { get; set; }
    public decimal TotalCreditLimit { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
