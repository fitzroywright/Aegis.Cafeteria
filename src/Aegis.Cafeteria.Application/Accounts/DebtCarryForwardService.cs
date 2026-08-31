namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class DebtCarryForwardService
{
    public CarriedForwardDebt? Create(PayrollCollectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.UnpaidAmount == 0m) return null;
        var source = new DateOnly(result.Year,result.Month,1);
        var next = source.AddMonths(1);
        return new CarriedForwardDebt(Guid.NewGuid(),result.CustomerId,result.Year,result.Month,next.Year,next.Month,result.UnpaidAmount,result.ReconciledAt);
    }
}
