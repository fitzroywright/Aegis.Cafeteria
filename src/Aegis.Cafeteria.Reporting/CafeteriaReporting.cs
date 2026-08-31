namespace Aegis.Cafeteria.Reporting;

using Aegis.Cafeteria.Reporting.Export;
using Microsoft.Extensions.DependencyInjection;

public static class CafeteriaReporting
{
    public static IServiceCollection AddCafeteriaReporting(this IServiceCollection services)
    {
        services.AddSingleton<ICafeteriaReportService, CafeteriaReportService>();
        services.AddSingleton<IReportExporter, CsvReportExporter>();
        services.AddSingleton<IReportExporter, HtmlReportExporter>();
        services.AddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton<IReportExporter, PdfReportExporter>();
        return services;
    }
}
