namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Reporting;

public sealed record TenderCustomerContext
(
      bool CafeteriaAccountAllowed
    , bool CreditSalesAllowed
    , decimal AvailableToSpend
    , decimal SubsidyRemaining
);

public static class TenderCustomerPolicy
{
    public static TenderCustomerContext Evaluate(CustomerAccountView account)
    {
        ArgumentNullException.ThrowIfNull(account);

        bool employeeTenderAllowed = !account.Customer.IsCashCustomer && account.Customer.IsEligibleForCredit;
        bool cafeteriaAccountAllowed = employeeTenderAllowed
            && account.FinancialStatus.CreditShortfall == 0m;

        return new TenderCustomerContext
        (
              CafeteriaAccountAllowed: cafeteriaAccountAllowed
            , CreditSalesAllowed: employeeTenderAllowed
            , AvailableToSpend: cafeteriaAccountAllowed ? Math.Max(0m, account.FinancialStatus.TotalAvailableToSpend) : 0m
            , SubsidyRemaining: cafeteriaAccountAllowed ? Math.Max(0m, account.FinancialStatus.SubsidyAmountRemaining) : 0m
        );
    }
}
