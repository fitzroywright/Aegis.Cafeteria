using Aegis.Cafeteria.Services.Development;
using Aegis.Cafeteria.Services.Workers;

namespace Aegis.Cafeteria.Services;

internal static class MonthOpeningEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/month-opening/run", RunMonthOpeningAsync).RequireAuthorization("CafeteriaManagement");
        app.MapPost("/api/month-opening/recalculate", RecalculateMonthAsync).RequireAuthorization("CafeteriaManagement");
        app.MapPost("/api/month-closing/retry", RetryMonthCloseAsync).RequireAuthorization("CafeteriaManagement");

        if (app.Environment.IsDevelopment())
            app.MapPost("/api/development/reset", ResetDevelopmentDataAsync).RequireAuthorization("CafeteriaManagement");
    }

    private static async Task<IResult> RunMonthOpeningAsync(int? year, int? month, MonthOpeningWorker worker, CancellationToken ct)
    {
        DateTime now = DateTime.Now;
        int selectedYear = year ?? now.Year;
        int selectedMonth = month ?? now.Month;
        if (selectedMonth is < 1 or > 12) return Results.BadRequest("Month must be 1-12.");
        return Results.Ok(new { created = await worker.EnsureMonthAsync(selectedYear, selectedMonth, ct), year = selectedYear, month = selectedMonth });
    }

    private static async Task<IResult> RecalculateMonthAsync(int? year, int? month, HttpContext http, MonthOpeningWorker worker, CancellationToken ct)
    {
        DateTime now = DateTime.Now;
        int selectedYear = year ?? now.Year;
        int selectedMonth = month ?? now.Month;
        if (selectedMonth is < 1 or > 12) return Results.BadRequest("Month must be 1-12.");
        try
        {
            MonthRecalculationResult result = await worker.RecalculateMonthAsync(selectedYear, selectedMonth, CafeteriaActor.Require(http), ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static async Task<IResult> RetryMonthCloseAsync(
        int? year,
        int? month,
        MonthClosingWorker worker,
        CancellationToken ct)
    {
        DateTime previous = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        int selectedYear = year ?? previous.Year;
        int selectedMonth = month ?? previous.Month;

        if (selectedYear < 2000 || selectedYear > 9999 || selectedMonth is < 1 or > 12)
        {
            return Results.BadRequest("A valid year and month are required.");
        }

        try
        {
            MonthCloseResult result = await worker.EnsureClosedAsync(selectedYear, selectedMonth, ct);
            return Results.Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static async Task<IResult> ResetDevelopmentDataAsync(HttpContext http, DevelopmentDataResetService reset, CancellationToken ct)
    {
        DevelopmentResetResult result = await reset.ResetAsync(CafeteriaActor.Require(http), ct);
        return Results.Ok(result);
    }
}