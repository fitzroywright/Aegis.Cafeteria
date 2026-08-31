namespace Aegis.Cafeteria.Reporting.Export;

using System.Globalization;
using System.Text;
using Aegis.Cafeteria.Reporting.Models;

public sealed class CsvReportExporter : IReportExporter
{
    public string Format => "CSV";
    public string FileExtension => ".csv";

    public async Task ExportAsync(ReportDocument report, Stream output, CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(true), leaveOpen: true);
        await writer.WriteLineAsync(Escape(report.Title));
        await writer.WriteLineAsync(Escape(report.Subtitle));
        await writer.WriteLineAsync();

        foreach (var metric in report.Metrics)
            await writer.WriteLineAsync($"{Escape(metric.Label)},{Escape(FormatDecimal(metric.Value, metric.Format))}");

        foreach (var table in report.Tables)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(Escape(table.Title));
            await writer.WriteLineAsync(string.Join(',', table.Columns.Select(c => Escape(c.Heading))));
            foreach (var row in table.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(string.Join(',', table.Columns.Select(c => Escape(FormatValue(row.GetValueOrDefault(c.Key), c.Format)))));
            }
        }
        await writer.FlushAsync(cancellationToken);
    }

    private static string FormatValue(object? value, string format) => value switch
    {
        null => string.Empty,
        decimal d => FormatDecimal(d, format),
        double d => FormatDecimal((decimal)d, format),
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    private static string FormatDecimal(decimal value, string format) => format switch
    {
        "currency" => value.ToString("0.00", CultureInfo.InvariantCulture),
        "percent" => value.ToString("0.##", CultureInfo.InvariantCulture),
        "integer" => value.ToString("0", CultureInfo.InvariantCulture),
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
