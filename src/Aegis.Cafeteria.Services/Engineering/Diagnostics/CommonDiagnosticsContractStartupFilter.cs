using System.Security.Cryptography;
using System.Text;
using Common.Diagnostics;
using LocalLevel = Aegis.Engineering.Diagnostics.EngineeringDiagnosticLevel;
using LocalRun = Aegis.Engineering.Diagnostics.EngineeringDiagnosticRun;
using LocalStatus = Aegis.Engineering.Diagnostics.EngineeringDiagnosticStatus;

namespace Aegis.Cafeteria.Services.Engineering.Diagnostics;

public sealed class CommonDiagnosticsContractStartupFilter : IStartupFilter
{
    private const string HeaderName = "X-Aegis-Diagnostics-Key";
    private const string EnvironmentVariable = "CAFETERIA_DIAGNOSTICS_KEY";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, continuePipeline) =>
            {
                if (!context.Request.Path.StartsWithSegments("/api/engineering/diagnostics") || !IsMachineAuthorized(context))
                {
                    await continuePipeline();
                    return;
                }

                await HandleMachineRequestAsync(context);
            });

            next(app);
        };
    }

    private static async Task HandleMachineRequestAsync(HttpContext context)
    {
        string path = context.Request.Path.Value ?? string.Empty;
        IServiceProvider services = context.RequestServices;
        EngineeringDiagnosticRunStore store = services.GetRequiredService<EngineeringDiagnosticRunStore>();

        if (HttpMethods.IsGet(context.Request.Method) && path.Equals("/api/engineering/diagnostics/runs", StringComparison.OrdinalIgnoreCase))
        {
            int take = int.TryParse(context.Request.Query["take"], out int requested) ? Math.Clamp(requested, 1, 100) : 25;
            IReadOnlyList<LocalRun> runs = await store.GetRecentAsync(take, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(runs.Select(ToCommon).ToArray(), context.RequestAborted);
            return;
        }

        const string prefix = "/api/engineering/diagnostics/runs/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string remainder = path[prefix.Length..];
            bool resolve = remainder.EndsWith("/resolve", StringComparison.OrdinalIgnoreCase);
            string idText = resolve ? remainder[..^"/resolve".Length] : remainder;
            if (!Guid.TryParse(idText.Trim('/'), out Guid runId))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (HttpMethods.IsGet(context.Request.Method) && !resolve)
            {
                LocalRun? run = await store.GetAsync(runId, context.RequestAborted);
                if (run is null) context.Response.StatusCode = StatusCodes.Status404NotFound;
                else await context.Response.WriteAsJsonAsync(ToCommon(run), context.RequestAborted);
                return;
            }

            if (HttpMethods.IsPost(context.Request.Method) && resolve)
            {
                EngineeringDiagnosticResolutionRequest? request = await context.Request.ReadFromJsonAsync<EngineeringDiagnosticResolutionRequest>(context.RequestAborted);
                if (request is null || string.IsNullOrWhiteSpace(request.Resolution))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(new { error = "Resolution is required." }, context.RequestAborted);
                    return;
                }

                LocalRun? resolved = await store.ResolveAsync(runId, "Aegis.Diagnostics", request.Resolution.Trim(), context.RequestAborted);
                if (resolved is null) context.Response.StatusCode = StatusCodes.Status404NotFound;
                else await context.Response.WriteAsJsonAsync(ToCommon(resolved), context.RequestAborted);
                return;
            }
        }

        if (HttpMethods.IsPost(context.Request.Method) && path.Equals("/api/engineering/diagnostics/run", StringComparison.OrdinalIgnoreCase))
        {
            EngineeringDiagnosticRunRequest? request = await context.Request.ReadFromJsonAsync<EngineeringDiagnosticRunRequest>(context.RequestAborted);
            if (request is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            try
            {
                CafeteriaEngineeringDiagnosticService diagnostics = services.GetRequiredService<CafeteriaEngineeringDiagnosticService>();
                LocalRun run = await diagnostics.RunAsync(ToLocal(request.Level), "Aegis.Diagnostics", request.Reason, context.RequestAborted);
                await context.Response.WriteAsJsonAsync(ToCommon(run), context.RequestAborted);
            }
            catch (ArgumentException exception)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = exception.Message }, context.RequestAborted);
            }
            return;
        }

        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static bool IsMachineAuthorized(HttpContext context)
    {
        string? expected = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(expected) || !context.Request.Headers.TryGetValue(HeaderName, out var supplied))
        {
            return false;
        }

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static LocalLevel ToLocal(EngineeringDiagnosticLevel level) => level switch
    {
        EngineeringDiagnosticLevel.Level5Scan => LocalLevel.Level5Scan,
        EngineeringDiagnosticLevel.Level4Analysis => LocalLevel.Level4Analysis,
        EngineeringDiagnosticLevel.Level3Verification => LocalLevel.Level3Verification,
        EngineeringDiagnosticLevel.Level2Repair => LocalLevel.Level2Repair,
        EngineeringDiagnosticLevel.Level1CriticalIntervention => LocalLevel.Level1CriticalIntervention,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private static EngineeringDiagnosticRun ToCommon(LocalRun run)
    {
        EngineeringDiagnosticCheckResult[] results = run.Checks.Select(check => new EngineeringDiagnosticCheckResult(
            check.CheckId,
            check.Name,
            ToCommon(check.Status),
            check.Summary,
            check.Evidence)).ToArray();

        return new EngineeringDiagnosticRun(
            run.RunId,
            run.StartedAt,
            run.CompletedAt,
            ToCommon(run.Level),
            run.Application,
            run.Environment,
            run.RequestedBy,
            run.Reason,
            results,
            run.ResolvedAt,
            run.ResolvedBy,
            run.Resolution);
    }

    private static EngineeringDiagnosticLevel ToCommon(LocalLevel level) => level switch
    {
        LocalLevel.Level5Scan => EngineeringDiagnosticLevel.Level5Scan,
        LocalLevel.Level4Analysis => EngineeringDiagnosticLevel.Level4Analysis,
        LocalLevel.Level3Verification => EngineeringDiagnosticLevel.Level3Verification,
        LocalLevel.Level2Repair => EngineeringDiagnosticLevel.Level2Repair,
        LocalLevel.Level1CriticalIntervention => EngineeringDiagnosticLevel.Level1CriticalIntervention,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    private static EngineeringDiagnosticStatus ToCommon(LocalStatus status) => status switch
    {
        LocalStatus.Passed => EngineeringDiagnosticStatus.Passed,
        LocalStatus.Warning => EngineeringDiagnosticStatus.Warning,
        LocalStatus.Failed => EngineeringDiagnosticStatus.Failed,
        LocalStatus.InterventionRequired => EngineeringDiagnosticStatus.Warning,
        _ => EngineeringDiagnosticStatus.Skipped
    };
}
