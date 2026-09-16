using Aegis.Engineering.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

namespace Aegis.Cafeteria.Services;

internal static class EngineeringEndpoints
{
    internal static void Map(WebApplication app)
    {
        MapLogin(app);
        MapDiagnostics(app);
    }

    private static void MapLogin(WebApplication app)
    {
        app.MapGet("/engineering/login", (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true
                ? Results.Redirect("/engineering/diagnostics")
                : Results.Content(EngineeringDiagnosticsLoginPage.Html, "text/html"));

        app.MapGet("/engineering/signin", async (HttpContext http) =>
        {
            if (http.User.Identity?.IsAuthenticated == true)
            {
                http.Response.Redirect("/engineering/diagnostics");
                return;
            }
            await http.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
        });
    }

    private static void MapDiagnostics(WebApplication app)
    {
        app.MapGet("/engineering/diagnostics", (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true
                ? Results.Content(LcarsDiagnosticsDashboard.Html, "text/html")
                : Results.Redirect("/engineering/login"))
            .RequireAuthorization("EngineeringDiagnostics");

        app.MapGet("/api/engineering/diagnostics/runs", async (int? take, EngineeringDiagnosticRunStore store, CancellationToken ct) =>
            Results.Ok(await store.GetRecentAsync(take ?? 25, ct)))
            .RequireAuthorization("EngineeringDiagnostics");

        app.MapGet("/api/engineering/diagnostics/runs/{runId:guid}", async (Guid runId, EngineeringDiagnosticRunStore store, CancellationToken ct) =>
        {
            EngineeringDiagnosticRun? run = await store.GetAsync(runId, ct);
            return run is null ? Results.NotFound() : Results.Ok(run);
        }).RequireAuthorization("EngineeringDiagnostics");

        app.MapPost("/api/engineering/diagnostics/runs/{runId:guid}/resolve", async (Guid runId, EngineeringDiagnosticResolutionRequest request, HttpContext http, EngineeringDiagnosticRunStore store, CancellationToken ct) =>
        {
            try
            {
                EngineeringDiagnosticRun? run = await store.ResolveAsync(runId, CafeteriaActor.Require(http), request.Resolution, ct);
                return run is null ? Results.NotFound() : Results.Ok(run);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        }).RequireAuthorization("EngineeringDiagnostics");

        app.MapPost("/api/engineering/diagnostics/run", async (EngineeringDiagnosticRunRequest request, HttpContext http, CafeteriaEngineeringDiagnosticService diagnostics, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await diagnostics.RunAsync(request.Level, CafeteriaActor.Require(http), request.Reason, ct));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        }).RequireAuthorization("EngineeringDiagnostics");
    }
}