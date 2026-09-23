namespace Aegis.Cafeteria.Reporting.Models;

public enum CafeteriaReportKind
{
    ExecutiveSummary,
    DailySales,
    SalesTrend,
    TenderMix,
    ProductSales,
    EmployeeStatement,
    CanteenSummary,
    MonthlyAllocations,
    SubsidyUtilization,
    CustomerResponsibility,
    RefundVoidAudit,
    AdditionalCreditAudit,
    OutstandingDebt,
    ExternalSystemReconciliation,
    PurchaseItemDetail,
    PurchaseByPerson
}

public enum ReportChartType { Line, Bar, Pie }

public enum PurchaseGrouping { Day, Week, Month }

public sealed record ReportFilter(
    DateOnly From,
    DateOnly To,
    int? EmployeeId = null,
    int? ProductId = null,
    bool IncludeVoided = false,
    PurchaseGrouping Grouping = PurchaseGrouping.Day);

public sealed record ReportMetric(string Label, decimal Value, string Format = "currency", string? Note = null);

public sealed record ReportColumn(string Key, string Heading, string Format = "text");

public sealed record ReportTable(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

public sealed record ReportChartSeries(string Name, IReadOnlyList<decimal> Values, string Format = "number");

public sealed record ReportChart(
    string Title,
    ReportChartType Type,
    IReadOnlyList<string> Labels,
    IReadOnlyList<ReportChartSeries> Series,
    string? Subtitle = null);

public sealed record ReportDocument(
    CafeteriaReportKind Kind,
    string Title,
    string Subtitle,
    DateTimeOffset GeneratedAt,
    ReportFilter Filter,
    IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<ReportChart> Charts,
    IReadOnlyList<ReportTable> Tables,
    IReadOnlyList<string>? Warnings = null,
    string? GeneratedBy = null);

public sealed record ReportCatalogItem(
    CafeteriaReportKind Kind,
    string Name,
    string Description,
    bool SupportsEmployeeFilter,
    bool SupportsProductFilter);
