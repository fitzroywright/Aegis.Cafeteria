namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;

/// <summary>
/// Shared Windows/Linux workflow for recording a voluntary customer payment against
/// current cafeteria responsibility. The payment is persisted locally first and, when
/// FFP publication is enabled, an idempotent ledger adjustment is queued afterwards.
/// </summary>
public sealed class CustomerPaymentWorkflow(
    ICafeteriaPersistenceStore persistenceStore,
    IExternalLedgerProjectionService? ledgerProjection = null)
{
    public async Task<CustomerPayment> RecordAsync(
        CustomerAccountView account,
        decimal amount,
        string paymentMethod,
        string receivedByUserId,
        string? reference = null,
        DateTimeOffset? receivedAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit)
            throw new InvalidOperationException("Customer payments are available only for cafeteria-credit customers.");
        if (account.Period.IsClosed)
            throw new InvalidOperationException("Customer payments cannot be posted into a closed cafeteria period.");
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        if (amount > account.FinancialStatus.EmployeeAmountDue)
            throw new InvalidOperationException("Payment cannot exceed the amount currently due from the customer.");
        if (string.IsNullOrWhiteSpace(paymentMethod))
            throw new ArgumentException("Payment method is required.", nameof(paymentMethod));
        if (string.IsNullOrWhiteSpace(receivedByUserId))
            throw new ArgumentException("Receiving operator is required.", nameof(receivedByUserId));

        DateTimeOffset at = receivedAt ?? DateTimeOffset.Now;
        decimal outstandingBalanceBefore = account.FinancialStatus.EmployeeAmountDue;
        decimal outstandingBalanceAfter = outstandingBalanceBefore - amount;
        CustomerPayment payment = new(
            Guid.NewGuid(),
            account.Customer.Id,
            amount,
            at,
            reference,
            paymentMethod,
            receivedByUserId,
            outstandingBalanceBefore,
            outstandingBalanceAfter);

        await persistenceStore.SaveCustomerPaymentAsync(payment, cancellationToken);

        if (ledgerProjection is not null)
        {
            decimal postBalance = Math.Min(
                account.FinancialStatus.TotalCreditLimit,
                account.FinancialStatus.TotalAvailableToSpend + amount);
            string description = string.IsNullOrWhiteSpace(reference)
                ? $"Customer payment - {payment.PaymentMethod}"
                : $"Customer payment - {payment.PaymentMethod} - {reference.Trim()}";

            await ledgerProjection.PublishAdjustmentAsync(
                account.Customer.Id,
                payment.Id,
                ExternalLedgerEntryKind.CustomerPayment,
                payment.ReceivedAt,
                description,
                amount,
                account.FinancialStatus.TotalCreditLimit,
                postBalance,
                cancellationToken,
                payment.ReceivedByUserId,
                "Customer Payment",
                "PAYMENT");
        }

        return payment;
    }
}
