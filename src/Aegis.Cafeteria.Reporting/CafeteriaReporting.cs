namespace Aegis.Cafeteria.Reporting;

using Aegis.Cafeteria.Reporting.Export;
using Microsoft.Extensions.DependencyInjection;

public static class CafeteriaReporting
{
    public static IServiceCollection AddCafeteriaReporting(this IServiceCollection services)
    {
        services.AddSingleton<CafeteriaReportService>();
        services.AddSingleton<ICafeteriaReportService, IDMethodReportDecorator>();
        services.AddSingleton<IReportExporter, CsvReportExporter>();
        services.AddSingleton<IReportExporter, HtmlReportExporter>();
        services.AddSingleton<IReportExporter, ExcelReportExporter>();
        services.AddSingleton<IReportExporter, PdfReportExporter>();
        return services;
    }
}
