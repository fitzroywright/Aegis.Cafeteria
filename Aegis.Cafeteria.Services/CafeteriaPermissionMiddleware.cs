namespace Aegis.Cafeteria.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

public static class CafeteriaPermissionMiddleware
{
    public static IApplicationBuilder UseCafeteriaPermissions(this IApplicationBuilder app) => app.Use(async(context,next)=>
    {
        string? permission=RequiredPermission(context.Request.Method,context.Request.Path.Value ?? "");
        if(permission is not null && context.User.Identity?.IsAuthenticated==true && !CafeteriaPermissions.HasPermission(context.User,permission))
        {context.Response.StatusCode=StatusCodes.Status403Forbidden;return;}
        await next(context);
    });

    internal static string? RequiredPermission(string method, string path)
    {
        if (path.StartsWith("/api/engineering/diagnostics", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/engineering/diagnostics", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.RunDiagnostics;

        if (path.Contains("/audit", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.ViewAudit;

        if (path.Contains("/backup", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.ManageBackups;

        if (path.Contains("/settings", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.ChangeSettings;

        if (path.Contains("/users", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/roles", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.ManageUsers;

        if (path.Contains("/catalog", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/items", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(method) ? CafeteriaPermissions.ViewReports : CafeteriaPermissions.ManageCatalog;

        if (path.Contains("additional-credit", StringComparison.OrdinalIgnoreCase)
            || path.Contains("credit-policy", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(method) ? CafeteriaPermissions.ViewReports : CafeteriaPermissions.AdditionalCredit;

        if (path.Contains("stipend", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(method) ? CafeteriaPermissions.ViewReports : CafeteriaPermissions.Stipend;

        if (path.Contains("refund", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.Refund;

        if (path.Contains("void", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.VoidTransaction;

        if (path.Contains("period", StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(method))
            return CafeteriaPermissions.ClosePeriod;

        if (path.Contains("report", StringComparison.OrdinalIgnoreCase))
            return CafeteriaPermissions.ViewReports;

        if (path.StartsWith("/api/management/", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(method) ? CafeteriaPermissions.ViewReports : CafeteriaPermissions.ManageOperations;

        return null;
    }
}
