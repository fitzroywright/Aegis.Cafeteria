namespace Aegis.Cafeteria.Application.Integration;

/// <summary>
/// Compatibility-write boundary for an external accounting projection.
/// Read-side workforce/reference data is intentionally separated behind IWorkforceReferenceSource.
/// </summary>
public interface IExternalAccountingGateway
{
    Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default);
}

public enum ExternalLedgerEntryKind
{
    MonthlyAllocation = 1,
    Purchase = 2,
    Refund = 3,
    CustomerPayment = 4,
    AdditionalCredit = 5,
    StipendCardReduction = 6,
    Adjustment = 7
}

/// <summary>
/// A single idempotent compatibility transaction. OperationId is written to
/// the external system idempotency key and therefore protects retries from duplication.
/// CreditLimit and AvailableBalance are the post-transaction external account summary values.
/// For purchase entries, Code is the Universal Search value, ScanCode is the IDMethod enum name,
/// UserName is the cashier display name, and CreatedBy is SYSTEM.
/// </summary>
public sealed record ExternalLedgerEntry(
    Guid OperationId,
    int CustomerId,
    ExternalLedgerEntryKind Kind,
    DateTimeOffset OccurredAt,
    string Description,
    decimal ScanValue,
    decimal CreditLimit,
    decimal AvailableBalance,
    string ScanCode = "AEGIS",
    string UserName = "SYSTEM",
    string? IdName = null,
    string? Code = null,
    string CreatedBy = "SYSTEM");
