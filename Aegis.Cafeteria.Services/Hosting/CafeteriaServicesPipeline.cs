using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Logging;
using Aegis.Cafeteria.Services.Persistence;
using Common.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aegis.Cafeteria.Services;

internal static class CafeteriaServicesPipeline
{
    internal static void ConfigureLogging(WebApplication app)
    {
        CafeteriaLoggingOptions options = app.Configuration.GetSection("Logging").Get<CafeteriaLoggingOptions>() ?? new CafeteriaLoggingOptions();
        app.Services.GetRequiredService<ILoggerFactory>().AddCafeteriaProviders(options);
    }

    internal static void Configure(WebApplication app)
    {
        ConfigureDevelopmentMiddleware(app);
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseManagementCommonSecurityAuthentication();
        global::Common.Security.Hosting.AccountSecurityHosting.UseCommonAccountEnforcement(app,"/api/pos/authenticate");
        app.UseAuthorization();
        app.UseCafeteriaAuditTrail();
        AuditTrailEndpoints.Map(app);
        MapHealth(app);
        app.MapOperatorSecurity();
    }

    private static void ConfigureDevelopmentMiddleware(WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseCors("DevelopmentManagement");
    }

    private static void MapHealth(WebApplication app)
    {
        app.MapAegisHealth("Aegis.Cafeteria.Services", async (services, cancellationToken) =>
        {
            IDbContextFactory<ServiceDbContext> factory = services.GetRequiredService<IDbContextFactory<ServiceDbContext>>();
            await using ServiceDbContext db = await factory.CreateDbContextAsync(cancellationToken);
            bool canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? AegisHealthAssessment.Healthy("Cafeteria Services and its service database are healthy.")
                : AegisHealthAssessment.Unhealthy("Cafeteria Services service database is unavailable.");
        });
    }
}
