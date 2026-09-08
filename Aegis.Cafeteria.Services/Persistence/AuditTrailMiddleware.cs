namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;

public sealed class AuditTrailMiddleware
{
    private static readonly HashSet<string> AuditedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
          HttpMethods.Post
        , HttpMethods.Put
        , HttpMethods.Patch
        , HttpMethods.Delete
    };

    private readonly RequestDelegate next;

    public AuditTrailMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync
    (
          HttpContext http
        , IDbContextFactory<ServiceDbContext> factory
        , IConfiguration configuration
    )
    {
        if (!AuditedMethods.Contains(http.Request.Method))
        {
            await this.next(http);
            return;
        }

        Exception? failure = null;
        try
        {
            await this.next(http);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            string actor = http.User.Identity?.IsAuthenticated == true
                ? http.User.Identity.Name ?? "AUTHENTICATED"
                : "ANONYMOUS";

            string action = $"{http.Request.Method} {http.Request.Path}";
            string entityType = ResolveEntityType(http.Request.Path);
            string? entityId = ResolveEntityId(http);
            int? customerId = ResolveCustomerId(http);
            Guid? operationId = ResolveOperationId(http);
            bool succeeded = failure is null && http.Response.StatusCode < 400;
            string? terminalId = http.Request.Headers["X-Aegis-Terminal-Id"].FirstOrDefault();
            CafeteriaLoggingOptions loggingOptions = configuration.GetSection("Logging").Get<CafeteriaLoggingOptions>()
                ?? new CafeteriaLoggingOptions();
            AuditTrailService auditTrail = new(factory, loggingOptions);

            try
            {
                await auditTrail.RecordAsync
                (
                      actor
                    , action
                    , entityType
                    , entityId
                    , customerId
                    , operationId
                    , "HTTP"
                    , terminalId
                    , http.TraceIdentifier
                    , succeeded
                    , failure is null ? http.Response.StatusCode : StatusCodes.Status500InternalServerError
                    , before: null
                    , after: null
                    , detail: failure?.GetType().Name
                    , cancellationToken: CancellationToken.None
                );
            }
            catch
            {
                // Auditing must not mask or replace the business response. Operational monitoring
                // will surface database failures independently.
            }
        }
    }

    private static string ResolveEntityType(PathString path)
    {
        string value = path.Value ?? string.Empty;
        if (value.Contains("additional-credit", StringComparison.OrdinalIgnoreCase)) return "AdditionalCredit";
        if (value.Contains("stipend", StringComparison.OrdinalIgnoreCase)) return "Stipend";
        if (value.Contains("funding-policy", StringComparison.OrdinalIgnoreCase)) return "FundingPolicy";
        if (value.Contains("credit-policy", StringComparison.OrdinalIgnoreCase)) return "CustomerCreditPolicy";
        if (value.Contains("payroll-report", StringComparison.OrdinalIgnoreCase)) return "PayrollReport";
        if (value.Contains("month-opening", StringComparison.OrdinalIgnoreCase)) return "MonthOpening";
        if (value.Contains("ffp/ledger", StringComparison.OrdinalIgnoreCase)) return "FfpLedger";
        if (value.Contains("email", StringComparison.OrdinalIgnoreCase)) return "Email";
        if (value.Contains("authenticate", StringComparison.OrdinalIgnoreCase)) return "Authentication";
        if (value.Contains("development/reset", StringComparison.OrdinalIgnoreCase)) return "DevelopmentReset";
        if (value.Contains("engineering", StringComparison.OrdinalIgnoreCase)) return "EngineeringDiagnostics";
        return "HttpOperation";
    }

    private static string? ResolveEntityId(HttpContext http)
    {
        foreach (string key in new[] { "customerId", "runId", "id" })
        {
            if (http.Request.RouteValues.TryGetValue(key, out object? value) && value is not null)
            {
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static int? ResolveCustomerId(HttpContext http)
    {
        if (http.Request.RouteValues.TryGetValue("customerId", out object? value)
            && int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out int customerId))
        {
            return customerId;
        }

        return null;
    }

    private static Guid? ResolveOperationId(HttpContext http)
    {
        string? header = http.Request.Headers["X-Aegis-Operation-Id"].FirstOrDefault();
        return Guid.TryParse(header, out Guid operationId) ? operationId : null;
    }
}

public static class AuditTrailApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCafeteriaAuditTrail(this IApplicationBuilder application)
    {
        return application.UseMiddleware<AuditTrailMiddleware>();
    }
}
