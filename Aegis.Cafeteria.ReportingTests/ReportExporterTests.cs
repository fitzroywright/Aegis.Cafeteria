using System.Text;
using Aegis.Cafeteria.Reporting.Export;
using Aegis.Cafeteria.Reporting.Models;

namespace Aegis.Cafeteria.ReportingTests;

public sealed class ReportExporterTests
{
    [Fact]
    public async Task HtmlExporter_RendersMetricsTableAndSvgCharts()
    {
        var report = Sample();
        await using var stream = new MemoryStream();
        await new HtmlReportExporter().ExportAsync(report, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("Cafeteria executive summary", html);
        Assert.Contains("Net sales", html);
        Assert.Contains("<svg", html);
        Assert.Contains("Daily detail", html);
    }

    [Fact]
    public async Task CsvExporter_RendersMetricsAndTables()
    {
        var report = Sample();
        await using var stream = new MemoryStream();
        await new CsvReportExporter().ExportAsync(report, stream);
        var csv = Encoding.UTF8.GetString(stream.ToArray());

        Assert.Contains("Net sales", csv);
        Assert.Contains("Daily detail", csv);
        Assert.Contains("Transactions", csv);
    }


    [Fact]
    public async Task ExcelExporter_WritesValidOpenXmlPackageWithSummaryAndReportSheets()
    {
        var report = Sample();
        await using var stream = new MemoryStream();
        await new ExcelReportExporter().ExportAsync(report, stream);
        stream.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);

        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet2.xml"));
    }

    [Fact]
    public async Task PdfExporter_WritesPdfHeaderAndReportTitle()
    {
        var report = Sample();
        await using var stream = new MemoryStream();
        await new PdfReportExporter().ExportAsync(report, stream);
        var text = Encoding.ASCII.GetString(stream.ToArray());

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("Cafeteria executive summary", text);
        Assert.Contains("Page 1 of", text);
    }

    [Theory]
    [InlineData(ReportChartType.Line)]
    [InlineData(ReportChartType.Bar)]
    [InlineData(ReportChartType.Pie)]
    public async Task HtmlExporter_RendersEverySupportedChartType(ReportChartType type)
    {
        var report = Sample(type);
        await using var stream = new MemoryStream();
        await new HtmlReportExporter().ExportAsync(report, stream);
        var html = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("<svg", html);
    }

    private static ReportDocument Sample(ReportChartType chartType = ReportChartType.Line) => new(
        CafeteriaReportKind.ExecutiveSummary,
        "Cafeteria executive summary",
        "01 Aug 2026 — 07 Aug 2026",
        new DateTimeOffset(2026, 8, 8, 8, 0, 0, TimeSpan.Zero),
        new ReportFilter(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7)),
        [new ReportMetric("Net sales", 12500m), new ReportMetric("Transactions", 42m, "integer")],
        [new ReportChart("Daily net sales", chartType, ["Mon", "Tue", "Wed"], [new ReportChartSeries("Sales", [3000m, 4500m, 5000m], "currency")])],
        [new ReportTable("Daily detail", [new ReportColumn("date", "Date"), new ReportColumn("count", "Transactions", "integer")], [new Dictionary<string, object?> { ["date"] = new DateOnly(2026, 8, 1), ["count"] = 10 }])]);
}
