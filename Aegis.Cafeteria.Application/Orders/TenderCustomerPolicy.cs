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

        bool allowed = !account.Customer.IsCashCustomer && account.Customer.IsEligibleForCredit;

        return new TenderCustomerContext
        (
              CafeteriaAccountAllowed: allowed
            , CreditSalesAllowed: allowed
            , AvailableToSpend: allowed ? Math.Max(0m, account.FinancialStatus.TotalAvailableToSpend) : 0m
            , SubsidyRemaining: allowed ? Math.Max(0m, account.FinancialStatus.SubsidyAmountRemaining) : 0m
        );
    }
}
