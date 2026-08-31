namespace Aegis.Cafeteria.Reporting;

using Aegis.Cafeteria.Reporting.Models;

public interface ICafeteriaReportService
{
    IReadOnlyList<ReportCatalogItem> Catalog { get; }
    Task<ReportDocument> GenerateAsync(CafeteriaReportKind kind, ReportFilter filter, CancellationToken cancellationToken = default);
}
