namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriodGuard
{
    public void EnsureOpen(AccountPeriod period, AccountPeriodClose? close)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (close is null) return;
        if (close.Period.Year == period.Year && close.Period.Month == period.Month)
            throw new InvalidOperationException("The account period is closed. Record an explicit later-period correction instead.");
    }
}
