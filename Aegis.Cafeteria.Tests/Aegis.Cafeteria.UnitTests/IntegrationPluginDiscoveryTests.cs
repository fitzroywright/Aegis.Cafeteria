using System.Reflection;
using Aegis.Cafeteria.Services.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Aegis.Cafeteria.UnitTests;

public sealed class IntegrationPluginDiscoveryTests
{
    private static Type Loader => typeof(MonthOpeningWorker).Assembly.GetType("Aegis.Cafeteria.Services.IntegrationPluginLoader")!;

    [Fact]
    public void Content_root_plugin_takes_precedence_over_application_base()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafeteria-plugin-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "plugins", "integration.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "path resolution only");
            Assert.Equal(path, Resolve("plugins/integration.dll", root));
            Assert.Equal(path, Resolve(path, root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Missing_content_root_plugin_falls_back_to_application_base()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafeteria-plugin-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "plugins", "integration.dll"), Resolve("plugins/integration.dll", root));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void Missing_plugin_reports_the_resolved_path_before_host_start()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Acceptance" });
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.dll");
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["IntegrationPlugin:AssemblyPath"] = path });
        var exception = Assert.Throws<TargetInvocationException>(() =>
            Loader.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]));
        Assert.Equal(path, Assert.IsType<FileNotFoundException>(exception.InnerException).FileName);
    }

    private static string Resolve(string path, string root) => (string)Loader
        .GetMethod("ResolveAssemblyPath", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [path, root])!;
}
