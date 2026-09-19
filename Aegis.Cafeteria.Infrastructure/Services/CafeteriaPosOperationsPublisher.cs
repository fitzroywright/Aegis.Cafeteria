using System.Net.Http.Headers;
using System.Net.Http.Json;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Common.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aegis.Cafeteria.Infrastructure.Services;

public sealed class CafeteriaPosOperationsPublisher(
    IConfiguration configuration,
    CafeteriaRuntimeOptions runtime,
    LocalPersistenceStore store,
    ILogger<CafeteriaPosOperationsPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.POS";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? operationsUrl = configuration["Aegis:Operations:Url"];
        if (string.IsNullOrWhiteSpace(operationsUrl))
        {
            logger.LogInformation("POS Operations telemetry is disabled because Aegis:Operations:Url is not configured.");
            return;
        }

        string instanceId = string.IsNullOrWhiteSpace(runtime.Persistence.TerminalId)
            ? Environment.MachineName
            : runtime.Persistence.TerminalId.Trim();
        string identityFile =
            configuration["Aegis:Registration:IdentityFile"]
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Aegis",
                    "Cafeteria.POS",
                    instanceId,
                    "registration-identity.json")
                : Path.Combine(
                    "/var/lib/aegis/cafeteria-pos",
                    SanitizePathSegment(instanceId),
                    "registration-identity.json"));

        var identityStore = new FileRegistrationIdentityStore(identityFile);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RegistrationIdentityDocument identity =
                    await identityStore.LoadOrCreateAsync(ApplicationId, instanceId, stoppingToken);

                if (!string.IsNullOrWhiteSpace(identity.Credential))
                {
                    PosSynchronizationSnapshot snapshot =
                        await store.GetPosSynchronizationSnapshotAsync(stoppingToken);

                    bool online = runtime.Services.Enabled;
                    string queueState = !snapshot.LocalStoreHealthy
                        ? "Failed"
                        : !online
                            ? "Offline"
                            : snapshot.Failed > 0
                                ? "Warning"
                                : snapshot.Pending > 0
                                    ? "Degraded"
                                    : "Healthy";

                    using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        operationsUrl.TrimEnd('/') + "/api/operations/synchronization/observe")
                    {
                        Content = JsonContent.Create(new
                        {
                            endpointId = instanceId,
                            applicationId = ApplicationId,
                            instanceId,
                            online,
                            queueState,
                            pending = snapshot.Pending,
                            failed = snapshot.Failed,
                            observedAtUtc = snapshot.ObservedAtUtc,
                            oldestPendingAtUtc = snapshot.OldestPendingAtUtc,
                            reason = BuildReason(snapshot, online)
                        })
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", Guid.NewGuid().ToString("N"));

                    using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogDebug(
                            "POS synchronization telemetry returned HTTP {StatusCode}.",
                            (int)response.StatusCode);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "POS synchronization telemetry publication failed; local transaction processing remains operational.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private static string BuildReason(PosSynchronizationSnapshot snapshot, bool online)
    {
        if (!snapshot.LocalStoreHealthy) return "Local POS store could not be inspected.";
        if (!online) return $"POS is operating offline with {snapshot.Pending} pending transaction(s).";
        if (snapshot.Failed > 0) return $"{snapshot.Failed} pending transaction(s) have synchronization errors and will retry.";
        if (snapshot.Pending > 0) return $"{snapshot.Pending} transaction(s) are queued for synchronization.";
        return "POS synchronization queue is empty.";
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
