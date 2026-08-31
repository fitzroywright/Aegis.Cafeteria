namespace Aegis.Cafeteria.Application.Reporting;

using Aegis.Cafeteria.Application.Abstractions;

/// <summary>
/// Creates the anonymous cash-only customer used by the POS. This is deliberately not an
/// employee and never participates in cafeteria credit, subsidy, employee-liability or FFP ledger projection.
/// </summary>
public static class CashCustomerAccountViewFactory
{
    public const int CashCustomerId = int.MaxValue;

    public static CustomerAccountView Create(DateOnly today)
    {
        var publication = new FfpPublicationStatus(false, 0, 0, null, null);
        return new CustomerAccountView(
            new CustomerIdentityView(CashCustomerId, "Cash Customer", null, false, true),
            new AccountPeriodView(today.Year, today.Month, 0, 0m, 0m, false),
            new CustomerFinancialStatusView(
                0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m),
            [],
            publication,
            new CustomerFundingStatusView(false, false, null, "Cash customer - cash tender only"));
    }
}
