namespace Aegis.Cafeteria.Domain.Services;

using Aegis.Cafeteria.Domain.Accounts;

public enum CafeteriaServiceEventKind
{
    AdditionalCredit = 1,
    Stipend = 2,
    MonthlyAllocation = 3,
    MonthlyAllocationRecalculation = 4,
    DevelopmentReset = 5,
    AccountPeriodClose = 6,
    ProductCatalogImport = 7,
    LegacyTransactionImport = 8
}

public sealed record CafeteriaServiceEventEnvelope(
    long Sequence,
    Guid OperationId,
    CafeteriaServiceEventKind Kind,
    int CustomerId,
    DateTimeOffset OccurredAt,
    string PayloadJson);

public sealed record AdditionalCreditServiceEvent(
    Guid OperationId,
    int CustomerId,
    decimal Amount,
    string ApprovedBy,
    DateTimeOffset ApprovedAt,
    string? Reason,
    decimal AdditionalCreditLimit);

public sealed record StipendServiceEvent(
    Guid OperationId,
    int CustomerId,
    decimal CashAmount,
    decimal CardReductionAmount,
    StipendPaymentMode PaymentMode,
    string ProcessedBy,
    DateTimeOffset ProcessedAt,
    string? ExpenseReportReference,
    string? Notes,
    int? FundingYear = null,
    int? FundingMonth = null);

public sealed record MonthlyAllocationServiceEvent(
    Guid OperationId,
    int CustomerId,
    int Year,
    int Month,
    int EligibleWorkingDays,
    bool IsEligibleForCredit,
    decimal CreditLimitPrice,
    decimal SubsidyCost,
    DateTimeOffset CalculatedAt,
    string EmployeeName);

public sealed record MonthlyAllocationRecalculationServiceEvent
(
      Guid OperationId
    , MonthlyAllocationServiceEvent PreviousAllocation
    , MonthlyAllocationServiceEvent ReplacementAllocation
    , string RecalculatedBy
    , DateTimeOffset RecalculatedAt
);

public sealed record DevelopmentResetServiceEvent
(
      Guid OperationId
    , string ResetBy
    , DateTimeOffset ResetAt
    , bool PreserveProducts
    , bool PreserveFundingPolicy
);

public sealed record AccountPeriodCloseServiceEvent
(
      Guid OperationId
    , int CustomerId
    , int Year
    , int Month
    , int WorkingDays
    , decimal EmployeeAmountDue
    , decimal ClosingAvailableCredit
    , decimal ClosingCompanyContributionRemaining
    , decimal ClosingStipendCardReduction
    , string ClosedBy
    , DateTimeOffset ClosedAt
)
{
    public string ProcessedBy => ClosedBy;
}


public sealed record ProductCatalogImportServiceEvent
(
      Guid OperationId
    , int SourceProductId
    , string SourceCode
    , string Name
    , decimal Price
    , string ImportedBy
    , DateTimeOffset ImportedAt
);

public sealed record LegacyTransactionLedgerSnapshot
(
      long SourceId
    , int CustomerId
    , DateTimeOffset OccurredAt
    , string IdName
    , string Description
    , decimal ScanValue
    , string UserName
    , string ScanCode
    , int ScanType
);

public sealed record LegacyTransactionImportServiceEvent
(
      Guid OperationId
    , long SourceId
    , int CustomerId
    , int Year
    , int Month
    , DateTimeOffset OccurredAt
    , string IdName
    , string Description
    , decimal ScanValue
    , string UserName
    , string ScanCode
    , int ScanType
    , string ImportedBy
    , DateTimeOffset ImportedAt
);
