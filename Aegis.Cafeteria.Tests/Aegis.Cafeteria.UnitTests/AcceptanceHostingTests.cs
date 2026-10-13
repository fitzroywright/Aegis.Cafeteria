using System.Reflection;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Cafeteria.UnitTests;

public sealed class AcceptanceHostingTests
{
    [Fact]
    public async Task Actual_acceptance_registration_constructs_workers_without_starting_any_automatic_worker()
    {
        string directory = Path.Combine(Path.GetTempPath(), "acceptance-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string root = FindRoot(); string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string plugin = Path.Combine(root, "Aegis.Cafeteria.Services", "bin", configuration, "net10.0", "plugins", "Aegis.Cafeteria.Plugin.Integration.dll");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Acceptance", ContentRootPath = directory });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServicePersistence:ConnectionString"] = "Data Source=" + Path.Combine(directory, "services.sqlite"),
                ["IntegrationPlugin:Enabled"] = "true", ["IntegrationPlugin:AssemblyPath"] = plugin,
                ["IntegrationPlugin:ConnectionString"] = "Server=127.0.0.1,1;Database=FFP;User Id=unit;Password=unit;Enlist=false;Connect Timeout=1"
            });
            var host = typeof(MonthOpeningWorker).Assembly.GetType("Aegis.Cafeteria.Services.CafeteriaServicesHost")!;
            host.GetMethod("ConfigureAcceptance", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
            Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(IHostedService));
            await using var app = builder.Build();
            var provider = app.Services.GetRequiredService<IEmployeeIdScanPublisher>();
            Assert.Equal("FfpManagerIntegrationProvider", provider.GetType().Name);
            Assert.NotNull(app.Services.GetRequiredService<MonthOpeningWorker>());
            Assert.NotNull(app.Services.GetRequiredService<MonthClosingWorker>());
            Assert.NotNull(app.Services.GetRequiredService<ExternalPublicationWorker>());
            var database = typeof(MonthOpeningWorker).Assembly.GetType("Aegis.Cafeteria.Services.CafeteriaServiceDatabase")!;
            await (Task)database.GetMethod("InitializeAsync", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [app, CancellationToken.None])!;
            await using var context = await app.Services.GetRequiredService<IDbContextFactory<ServiceDbContext>>().CreateDbContextAsync();
            Assert.True(await context.Database.CanConnectAsync());
            Assert.Empty(await context.Events.ToArrayAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
    private static string FindRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "Aegis.Cafeteria.Services"))) return current.FullName;
        throw new DirectoryNotFoundException();
    }
}
