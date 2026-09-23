namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Reporting;

public sealed record CustomerPaymentEligibility
(
      bool Allowed
    , decimal MaximumPayment
    , string? Reason
);

public static class CustomerPaymentPolicy
{
    public static CustomerPaymentEligibility Evaluate(CustomerAccountView account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit)
        {
            return new CustomerPaymentEligibility(false, 0m, "Customer payments are available only for cafeteria-credit customers.");
        }

        if (account.Period.IsClosed)
        {
            return new CustomerPaymentEligibility(false, 0m, "Customer payments cannot be posted into a closed cafeteria period.");
        }

        if (account.FinancialStatus.EmployeeAmountDue <= 0m)
        {
            return new CustomerPaymentEligibility(false, 0m, "This customer has no outstanding cafeteria responsibility to pay.");
        }

        return new CustomerPaymentEligibility(true, account.FinancialStatus.EmployeeAmountDue, null);
    }

    public static decimal RemainingAmount(decimal amountDue, decimal paymentAmount)
    {
        if (amountDue < 0m) throw new ArgumentOutOfRangeException(nameof(amountDue));
        if (paymentAmount < 0m) throw new ArgumentOutOfRangeException(nameof(paymentAmount));
        return Math.Max(0m, amountDue - paymentAmount);
    }

    public static bool IsValidPayment(decimal amountDue, decimal paymentAmount)
        => amountDue > 0m && paymentAmount > 0m && paymentAmount <= amountDue;
}
