namespace Aegis.Cafeteria.Presentation;

using Aegis.Cafeteria.Application.Reporting;

public static class CafeteriaDisplayText
{
    public const string EmployeeCard = "Employee Card";
    public const string EmployeeCredit = "Employee Credit";
    public const string Cash = "Cash";

    public static string Eligibility(CustomerAccountView account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return account.Customer.IsCashCustomer
            ? "CASH CUSTOMER"
            : account.Customer.IsEligibleForCredit
                ? "ELIGIBLE FOR CAFETERIA CREDIT"
                : "NOT ELIGIBLE FOR CAFETERIA CREDIT";
    }

    public static string Period(CustomerAccountView account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.Customer.IsCashCustomer)
        {
            return "Cash-only sale";
        }

        string month = new DateTime(account.Period.Year, account.Period.Month, 1).ToString("MMMM yyyy");
        return $"{month} • {account.Period.EligibleWorkingDays} eligible work days";
    }

    public static string TransactionTender(decimal accountAmount, decimal creditAmount, decimal cashAmount)
    {
        List<string> parts = [];

        if (accountAmount > 0m)
        {
            parts.Add($"{EmployeeCard} {CafeteriaDisplay.Money(accountAmount)}");
        }

        if (creditAmount > 0m)
        {
            parts.Add($"{EmployeeCredit} {CafeteriaDisplay.Money(creditAmount)}");
        }

        if (cashAmount > 0m)
        {
            parts.Add($"{Cash} {CafeteriaDisplay.Money(cashAmount)}");
        }

        return parts.Count == 0 ? "No tender" : string.Join(" • ", parts);
    }
}
