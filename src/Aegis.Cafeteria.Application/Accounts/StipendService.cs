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
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(account);

        if (!this.authorizer.CanProcessStipend(processedByUserId))
            throw new UnauthorizedAccessException("The user is not authorized to process cafeteria stipends.");
        if (cashAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(cashAmount));

        var cardReduction = Math.Min(cashAmount, account.RemainingCompanyContribution);
        cardReduction = Math.Min(cardReduction, account.AvailableCredit);

        return new Stipend(
            Guid.NewGuid(),
            customer.Id,
            cashAmount,
            cardReduction,
            paymentMode,
            processedByUserId,
            processedAt,
            expenseReportReference,
            notes);
    }
}
