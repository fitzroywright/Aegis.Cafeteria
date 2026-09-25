using Common.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Common.Registration;

namespace Aegis.Cafeteria.Services;

public sealed record CafeteriaFlowTelemetryItem(
    Guid OperationId,
    string FlowType,
    string? BusinessId,
    DateTimeOffset OccurredAt);

public sealed class CafeteriaOperationsFlowPublisher(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<CafeteriaOperationsFlowPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";
    private readonly Channel<CafeteriaFlowTelemetryItem> queue =
        Channel.CreateBounded<CafeteriaFlowTelemetryItem>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryEnqueueCommitted(
        Guid operationId,
        string flowType,
        string? businessId,
        DateTimeOffset occurredAt)
        => queue.Writer.TryWrite(new(operationId, flowType, businessId, occurredAt));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (CafeteriaFlowTelemetryItem item in queue.Reader.ReadAllAsync(stoppingToken))
            await PublishCommittedAsync(item, stoppingToken);
    }

    private async Task PublishCommittedAsync(
        CafeteriaFlowTelemetryItem item,
        CancellationToken cancellationToken)
    {
        string? operationsUrl = configuration["Aegis:Operations:Url"];
        if (string.IsNullOrWhiteSpace(operationsUrl)) return;

        string instanceId = configuration["Service:Identity"]?.Trim() ?? Environment.MachineName;
        string identityFile = configuration["Aegis:Registration:IdentityFile"]
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Aegis",
                    "Cafeteria.Services",
                    "registration-identity.json")
                : "/var/lib/aegis/cafeteria-services/registration-identity.json");

        try
        {
            var store = new FileRegistrationIdentityStore(identityFile);
            RegistrationIdentityDocument identity =
                await store.LoadOrCreateAsync(ApplicationId, instanceId, cancellationToken);
            if (string.IsNullOrWhiteSpace(identity.Credential)) return;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            using HttpClient lifecycleClient = new() { Timeout = TimeSpan.FromSeconds(5) };
            var lifecycleSink = new HttpLifecycleEventSink(
                lifecycleClient,
                new HttpLifecycleEventSinkOptions(new Uri(operationsUrl.TrimEnd('/') + "/api/operations/lifecycle/events")),
                (request, lifecycleEvent, _) =>
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
                    return Task.CompletedTask;
                });
            await new LifecycleTelemetry(lifecycleSink).EmitAsync(
                LifecycleEvent.Create(
                    ApplicationId,
                    instanceId,
                    item.FlowType,
                    "Acknowledged",
                    LifecycleEventOutcome.Succeeded,
                    item.OperationId.ToString("D"),
                    operationId: item.OperationId.ToString("D"),
                    relatedBusinessId: item.BusinessId,
                    occurredAtUtc: now),
                cancellationToken);

            var payload = new
            {
                correlationId = item.OperationId.ToString("D"),
                applicationId = ApplicationId,
                flowType = item.FlowType,
                instanceId,
                startedAtUtc = item.OccurredAt,
                observedAtUtc = now,
                currentStage = "Acknowledged",
                relatedBusinessId = item.BusinessId,
                runbook = "docs/RUNBOOK.md",
                stages = new object[]
                {
                    new { name = "POS", state = "Unknown", error = (string?)null },
                    new { name = "LocalStore", state = "Unknown", error = (string?)null },
                    new { name = "Services", state = "Completed", startedAtUtc = (DateTimeOffset?)null, completedAtUtc = (DateTimeOffset?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null },
                    new { name = "Processing", state = "Completed", startedAtUtc = (DateTimeOffset?)null, completedAtUtc = (DateTimeOffset?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null },
                    new { name = "Database", state = "Completed", startedAtUtc = (DateTimeOffset?)null, completedAtUtc = (DateTimeOffset?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null },
                    new { name = "Acknowledged", state = "Completed", startedAtUtc = now, completedAtUtc = now, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null }
                }
            };

            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                operationsUrl.TrimEnd('/') + "/api/operations/flows/observe")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
            request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
            request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
            request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
            request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", item.OperationId.ToString("N"));

            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogDebug("Cafeteria Operations flow publication returned HTTP {StatusCode}.", (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Cafeteria Operations flow publication failed; transaction remains committed.");
        }
    }
}
