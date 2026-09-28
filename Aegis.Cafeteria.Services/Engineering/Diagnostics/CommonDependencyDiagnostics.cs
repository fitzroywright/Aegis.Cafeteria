using System.Diagnostics;
using System.Net.Sockets;
using Common.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
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

        bool integrationEnabled = configuration.GetValue("IntegrationPlugin:Enabled", false);
        if (!integrationEnabled)
            return (DiagnosticStatus.Warning, "Service database is healthy; External integration plugin is disabled.");

        string? connectionString = configuration["IntegrationPlugin:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
            return (DiagnosticStatus.Unhealthy, "External integration is enabled but no integration connection string is configured.");

        SqlConnectionStringBuilder builder = new(connectionString);
        string serverValue = builder.DataSource?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(serverValue))
            return (DiagnosticStatus.Unhealthy, "External integration is enabled but the SQL data source is empty.");

        string host = serverValue.Replace("tcp:", string.Empty, StringComparison.OrdinalIgnoreCase).Split(',')[0].Trim();
        int port = 1433;
        string[] parts = serverValue.Split(',');
        if (parts.Length > 1 && int.TryParse(parts[1], out int parsedPort))
            port = parsedPort;

        using TcpClient client = new();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(host, port, timeout.Token);

        return (DiagnosticStatus.Healthy, $"Service database is healthy; External integration is enabled and SQL endpoint {host}:{port} is reachable.");
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

public sealed class CafeteriaMessagingDiagnostic(
    Aegis.Cafeteria.Services.Email.EmailDeliveryOptions options,
    Common.Messaging.IChannelSecretResolver secrets) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Messaging";
    protected override async Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        string? error = options.ConfigurationError();
        if (error is not null)
        {
            return (options.Enabled ? DiagnosticStatus.Unhealthy : DiagnosticStatus.Warning, error);
        }

        string? secret = await secrets.GetSecretAsync(options.ClientSecretName, cancellationToken);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return (DiagnosticStatus.Unhealthy, "Microsoft 365 client secret could not be resolved through Common.Secrets.");
        }

        return (DiagnosticStatus.Unknown, "Common.Messaging Microsoft Graph settings and secret are present. Use Test Email and verify the mailbox to prove delivery.");
    }
}

public sealed class CafeteriaAuthenticationProviderDiagnostic(IConfiguration configuration) : CafeteriaDependencyDiagnosticBase
{
    public override string Name => "Authentication";
    protected override Task<(DiagnosticStatus, string)> CheckAsync(CancellationToken cancellationToken)
    {
        bool enabled = configuration.GetValue("AuthenticationPlugin:Enabled", true);
        if (!enabled)
            return Task.FromResult((DiagnosticStatus.Unhealthy, "Authentication plugin is disabled."));

        string assemblyPath = configuration["AuthenticationPlugin:AssemblyPath"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return Task.FromResult((DiagnosticStatus.Unhealthy, "AuthenticationPlugin:AssemblyPath is not configured."));

        return Task.FromResult((
            DiagnosticStatus.Unknown,
            $"Authentication plugin '{assemblyPath}' is configured. Provider-specific connectivity is verified by Common.Security/provider diagnostics."));
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

