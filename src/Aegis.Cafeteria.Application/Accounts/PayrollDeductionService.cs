namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class PayrollDeductionService
{
    public PayrollDeductionRecord Create(AccountPeriodClose close)
    {
        ArgumentNullException.ThrowIfNull(close);
        return new PayrollDeductionRecord(close.CustomerId, close.Period.Year, close.Period.Month, close.PayrollDeduction, close.Id);
    }
}
