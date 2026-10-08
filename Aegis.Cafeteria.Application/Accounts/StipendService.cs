namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class StipendService
{
    private readonly IStipendAuthorizer authorizer;

    public StipendService(IStipendAuthorizer authorizer)
    {
        this.authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
    }

    public Stipend Process(
        Customer customer,
        CustomerAccountSnapshot account,
        decimal cashAmount,
        StipendPaymentMode paymentMode,
        string processedByUserId,
        DateTimeOffset processedAt,
        string? expenseReportReference = null,
        string? notes = null,
        int? fundingYear = null,
        int? fundingMonth = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(account);

        if (!this.authorizer.CanProcessStipend(processedByUserId))
            throw new UnauthorizedAccessException("The user is not authorized to process cafeteria stipends.");
        if (cashAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(cashAmount));

        // A stipend is an exact debit against available cafeteria credit, never a cash reimbursement.
        // Reject the entire operation if it would overdraw the employee account.
        if (cashAmount > account.AvailableCredit)
            throw new InvalidOperationException("Insufficient available cafeteria credit for the requested stipend deduction.");

        decimal cardReduction = cashAmount;

        return new Stipend(
            Guid.NewGuid(),
            customer.Id,
            cashAmount,
            cardReduction,
            paymentMode,
            processedByUserId,
            processedAt,
            expenseReportReference,
            notes,
            fundingYear,
            fundingMonth);
    }
}
