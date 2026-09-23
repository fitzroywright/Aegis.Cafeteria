using System.Diagnostics;
using System.Net.Sockets;
using Common.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Aegis.Cafeteria.Services.Persistence;

namespace Aegis.Cafeteria.Services.Engineering.Diagnostics;

public abstract class CafeteriaDependencyDiagnosticBase : IDiagnosticCheck
{
    public abstract string Name { get; }
    protected abstract Task<(DiagnosticStatus Status, string Message)> CheckAsync(CancellationToken cancellationToken);

    public async Task<DiagnosticResult> RunAsync(CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            (DiagnosticStatus status, string message) = await CheckAsync(cancellationToken);
            stopwatch.Stop();
            return new DiagnosticResult(Name, status, message, stopwatch.Elapsed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new DiagnosticResult(Name, DiagnosticStatus.Unhealthy, exception.Message, stopwatch.Elapsed, exception);
        }
    }
}

public sealed class CafeteriaDatabaseDiagnostic(IDbContextFactory<ServiceDbContext> dbFactory, IConfiguration configuration) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Database";
    protected override async Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        await using ServiceDbContext db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Database.CanConnectAsync(cancellationToken))
            return (DiagnosticStatus.Unhealthy, "Cafeteria service database is unavailable.");

        bool ffpEnabled = configuration.GetValue("FfpIntegration:Enabled", false);
        if (!ffpEnabled) return (DiagnosticStatus.Warning, "Service database is healthy; FFP SQL integration is disabled.");
        string serverValue = configuration["FfpIntegration:Server"] ?? string.Empty;
        string host = serverValue.Replace("tcp:", string.Empty, StringComparison.OrdinalIgnoreCase).Split(',')[0];
        int port = 1433;
        string[] parts = serverValue.Split(',');
        if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort)) port = parsedPort;
        using TcpClient client = new();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(host, port, timeout.Token);
        return (DiagnosticStatus.Healthy, $"Service database is healthy and FFP SQL endpoint {host}:{port} is reachable.");
    }
}

public sealed class CafeteriaSecretsDiagnostic(IConfiguration configuration) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Secrets";
    protected override Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        bool configured = configuration.GetSection("CommonSecrets").Exists();
        return Task.FromResult(configured
            ? (DiagnosticStatus.Unknown, "Secrets are configured, but this check has no provider-health evidence. Provider availability must be verified through Secrets diagnostics.")
            : (DiagnosticStatus.Warning, "Cafeteria Services has no Secrets configuration in this host."));
    }
}

public sealed class CafeteriaStorageDiagnostic(IConfiguration configuration) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Storage";
    protected override async Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        string serviceDb = configuration["ServicePersistence:ConnectionString"] ?? "Data Source=cafeteria.services.db";
        Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new(serviceDb);
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
            return (DiagnosticStatus.Warning, "Service storage is in-memory; persistent storage verification is not applicable.");
        string fullPath = Path.GetFullPath(builder.DataSource);
        string directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);
        string probe = Path.Combine(directory, $".cafeteria-diagnostic-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(probe, "storage probe", cancellationToken);
        File.Delete(probe);
        return (DiagnosticStatus.Healthy, "Cafeteria service storage passed a write/delete probe.");
    }
}

public sealed class CafeteriaMessagingDiagnostic(IConfiguration configuration) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Messaging";
    protected override async Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        bool enabled = configuration.GetValue("ScheduledEmail:Enabled", false);
        if (!enabled) return (DiagnosticStatus.Warning, "Scheduled email is disabled.");
        string? host = configuration["ScheduledEmail:SmtpHost"];
        int port = configuration.GetValue("ScheduledEmail:SmtpPort", 25);
        if (string.IsNullOrWhiteSpace(host)) return (DiagnosticStatus.Unhealthy, "Scheduled email is enabled but SMTP host is missing.");
        using TcpClient client = new();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(host, port, timeout.Token);
        return (DiagnosticStatus.Healthy, $"SMTP endpoint {host}:{port} is reachable.");
    }
}

public sealed class CafeteriaActiveDirectoryDiagnostic(IConfiguration configuration) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Active Directory";
    protected override async Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        string[] servers = configuration.GetSection("Security:Servers").Get<string[]>() ?? [];
        int port = configuration.GetValue("Security:Port", 636);
        if (servers.Length == 0) return (DiagnosticStatus.Warning, "No Active Directory servers are configured for Cafeteria security.");
        foreach (string server in servers)
        {
            try
            {
                using TcpClient client = new();
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(server, port, timeout.Token);
                return (DiagnosticStatus.Healthy, $"Active Directory endpoint {server}:{port} is reachable.");
            }
            catch when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
        return (DiagnosticStatus.Unhealthy, $"No configured Active Directory server is reachable on TCP/{port}.");
    }
}

public sealed class CafeteriaDependencyDiagnosticHostedService(IServiceScopeFactory scopeFactory, ILogger<CafeteriaDependencyDiagnosticHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        do
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            IDiagnosticRunner runner = scope.ServiceProvider.GetRequiredService<IDiagnosticRunner>();
            IReadOnlyList<DiagnosticResult> results = await runner.RunAsync(stoppingToken);
            foreach (DiagnosticResult result in results)
            {
                LogLevel level = result.Status == DiagnosticStatus.Healthy ? LogLevel.Information : result.Status == DiagnosticStatus.Warning ? LogLevel.Warning : LogLevel.Error;
                logger.Log(level, "Dependency diagnostic {Name}: {Status} - {Message}", result.Name, result.Status, result.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
