namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriodFactory(IWorkingDayProvider workingDayProvider)
{
    public async Task<AccountPeriod> CreateAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var workingDays = await workingDayProvider.GetWorkingDaysAsync(year, month, cancellationToken);
        return new AccountPeriod(year, month, workingDays);
    }
}
