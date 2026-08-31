namespace Aegis.Cafeteria.Domain.Accounts;

public sealed record PayrollCollectionResult
{
    public PayrollCollectionResult(int customerId,int year,int month,decimal expectedDeduction,decimal collectedAmount,string reconciledBy,DateTimeOffset reconciledAt)
    {
        if(customerId<=0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if(month is <1 or >12) throw new ArgumentOutOfRangeException(nameof(month));
        if(expectedDeduction<0) throw new ArgumentOutOfRangeException(nameof(expectedDeduction));
        if(collectedAmount<0 || collectedAmount>expectedDeduction) throw new ArgumentOutOfRangeException(nameof(collectedAmount));
        if(string.IsNullOrWhiteSpace(reconciledBy)) throw new ArgumentException("Reconciled user is required.",nameof(reconciledBy));
        CustomerId=customerId; Year=year; Month=month; ExpectedDeduction=expectedDeduction; CollectedAmount=collectedAmount; ReconciledBy=reconciledBy.Trim(); ReconciledAt=reconciledAt;
    }
    public int CustomerId { get; }
    public int Year { get; }
    public int Month { get; }
    public decimal ExpectedDeduction { get; }
    public decimal CollectedAmount { get; }
    public decimal UnpaidAmount => ExpectedDeduction-CollectedAmount;
    public string ReconciledBy { get; }
    public DateTimeOffset ReconciledAt { get; }
}
