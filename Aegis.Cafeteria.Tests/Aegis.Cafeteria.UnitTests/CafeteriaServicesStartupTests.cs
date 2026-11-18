using System.Reflection;
using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using Aegis.Cafeteria.Services.Workers;
using Common.Diagnostics;
using Common.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Cafeteria.UnitTests;

public sealed class CafeteriaServicesStartupTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Normal_host_validates_dependencies_without_a_CommonMessaging_queue(string environment)
    {
        string directory = Path.Combine(Path.GetTempPath(), "cafeteria-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string root = FindRoot();
            string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            string plugin = Path.Combine(root, "Aegis.Cafeteria.Services", "bin", configuration, "net11.0", "plugins", "Aegis.Cafeteria.Plugin.Integration.dll");
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = directory
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServicePersistence:ConnectionString"] = "Data Source=" + Path.Combine(directory, "services.sqlite"),
                ["Aegis:Diagnostics:StorePath"] = Path.Combine(directory, "diagnostics.sqlite"),
                ["Aegis:Registration:IdentityFile"] = Path.Combine(directory, "identity.json"),
                ["Aegis:Configuration:Url"] = "http://127.0.0.1:1",
                ["CommonSecrets:Mode"] = "Development",
                ["CommonSecrets:ProviderOrder:0"] = "Environment",
                ["CommonSecrets:Providers:Environment:Type"] = "Environment",
                ["IntegrationPlugin:Enabled"] = "true",
                ["IntegrationPlugin:AssemblyPath"] = plugin,
                ["IntegrationPlugin:ConnectionString"] = "Server=127.0.0.1,1;Database=FFP;User Id=unit;Password=unit;Enlist=false;Connect Timeout=1"
            });
            builder.Host.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            });
            var host = typeof(MonthOpeningWorker).Assembly.GetType("Aegis.Cafeteria.Services.CafeteriaServicesHost")!;
            host.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);

            await using var app = builder.Build();
            using var scope = app.Services.CreateScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDiagnosticRunner>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<ILeveledDiagnosticRunner>());
            var checks = scope.ServiceProvider.GetServices<IDiagnosticLevelLocalTest>().ToArray();
            Assert.Contains(checks, check => check is CafeteriaMessagingDiagnosticLevelTest);
            Assert.Contains(checks, check => check is CafeteriaOutboundEmailQueueDiagnosticLevelTest);
            Assert.Null(app.Services.GetService<IExternalDeliveryQueueHealth>());
            Assert.Null(app.Services.GetService<ExternalDeliveryOptions>());
        }
        finally
        {
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
