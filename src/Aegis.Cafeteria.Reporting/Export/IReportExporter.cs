namespace Aegis.Cafeteria.Reporting.Export;

using Aegis.Cafeteria.Reporting.Models;

public interface IReportExporter
{
    string Format { get; }
    string FileExtension { get; }
    Task ExportAsync(ReportDocument report, Stream output, CancellationToken cancellationToken = default);
}
