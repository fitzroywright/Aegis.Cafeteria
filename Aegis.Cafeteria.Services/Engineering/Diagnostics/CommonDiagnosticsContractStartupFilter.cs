using System.Security.Cryptography;
using System.Text;
using Common.Diagnostics;
using LocalDiagnosticService = Aegis.Engineering.Diagnostics.CafeteriaEngineeringDiagnosticService;
using LocalRunStore = Aegis.Engineering.Diagnostics.EngineeringDiagnosticRunStore;
using LocalLevel = Common.Diagnostics.EngineeringDiagnosticLevel;
using LocalRun = Common.Diagnostics.EngineeringDiagnosticRun;
using LocalStatus = Common.Diagnostics.EngineeringDiagnosticStatus;

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
                if (!context.Request.Path.StartsWithSegments("/api/engineering/diagnostics"))
                {
                    await continuePipeline();
                    return;
                }

                if (!IsMachineAuthorized(context))
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
        LocalRunStore store = services.GetRequiredService<LocalRunStore>();

        if (HttpMethods.IsGet(context.Request.Method)
            && path.Equals("/api/engineering/diagnostics/runs", StringComparison.OrdinalIgnoreCase))
        {
            int take = int.TryParse(context.Request.Query["take"], out int requested)
                ? Math.Clamp(requested, 1, 100)
                : 25;
            IReadOnlyList<LocalRun> runs = await store.GetRecentAsync(take, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(runs.ToArray(), context.RequestAborted);
            return;
        }

        const string runsPrefix = "/api/engineering/diagnostics/runs/";
        if (path.StartsWith(runsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string remainder = path[runsPrefix.Length..];
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
                if (run is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
                else
                {
                    await context.Response.WriteAsJsonAsync(run, context.RequestAborted);
                }
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

                LocalRun? resolved = await store.ResolveAsync(
                    runId,
                    "Aegis.Diagnostics",
                    request.Resolution.Trim(),
                    context.RequestAborted);
                if (resolved is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
                else
                {
                    await context.Response.WriteAsJsonAsync(resolved, context.RequestAborted);
                }
                return;
            }
        }

        if (HttpMethods.IsPost(context.Request.Method)
            && path.Equals("/api/engineering/diagnostics/run", StringComparison.OrdinalIgnoreCase))
        {
            EngineeringDiagnosticRunRequest? request = await context.Request.ReadFromJsonAsync<EngineeringDiagnosticRunRequest>(context.RequestAborted);
            if (request is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            try
            {
                EngineeringDiagnosticPolicy.ValidateLevel(request.Level);
                EngineeringDiagnosticPolicy.ValidateReason(request.Level, request.Reason);

                LocalDiagnosticService diagnostics = services.GetRequiredService<LocalDiagnosticService>();
                LocalRun run = await diagnostics.RunAsync(
                    request.Level,
                    "Aegis.Diagnostics",
                    request.Reason,
                    context.RequestAborted);
                await context.Response.WriteAsJsonAsync(run, context.RequestAborted);
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
        if (string.IsNullOrWhiteSpace(expected)
            || !context.Request.Headers.TryGetValue(HeaderName, out Microsoft.Extensions.Primitives.StringValues supplied))
        {
            return false;
        }

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
        return expectedBytes.Length == suppliedBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
