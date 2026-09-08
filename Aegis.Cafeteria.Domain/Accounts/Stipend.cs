namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class Stipend
{
    public Stipend(
        Guid id,
        int customerId,
        decimal cashAmount,
        decimal cardReduction,
        StipendPaymentMode paymentMode,
        string processedByUserId,
        DateTimeOffset processedAt,
        string? expenseReportReference = null,
        string? notes = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Stipend ID is required.", nameof(id));
        if (customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(customerId));
        if (cashAmount <= 0)
            throw new ArgumentOutOfRangeException(nameof(cashAmount));
        if (cardReduction < 0)
            throw new ArgumentOutOfRangeException(nameof(cardReduction));
        if (cardReduction > cashAmount)
            throw new ArgumentException("Card reduction cannot exceed the cash amount.", nameof(cardReduction));
        if (string.IsNullOrWhiteSpace(processedByUserId))
            throw new ArgumentException("The Accounts user is required.", nameof(processedByUserId));

        Id = id;
        CustomerId = customerId;
        CashAmount = cashAmount;
        CardReduction = cardReduction;
        PaymentMode = paymentMode;
        ProcessedByUserId = processedByUserId.Trim();
        ProcessedAt = processedAt;
        ExpenseReportReference = string.IsNullOrWhiteSpace(expenseReportReference)
            ? null
            : expenseReportReference.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public decimal CashAmount { get; }
    public decimal CardReduction { get; }
    public StipendPaymentMode PaymentMode { get; }
    public string ProcessedByUserId { get; }
    public DateTimeOffset ProcessedAt { get; }
    public string? ExpenseReportReference { get; }
    public string? Notes { get; }
}
