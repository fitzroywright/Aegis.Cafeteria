using System.Net.Http.Headers;
using System.Net.Http.Json;
using Common.Registration;

namespace Aegis.Cafeteria.Services;

public sealed class CafeteriaOperationsFlowPublisher(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<CafeteriaOperationsFlowPublisher> logger)
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";

    public async Task PublishCommittedAsync(
        Guid operationId,
        string flowType,
        string? businessId,
        DateTimeOffset occurredAt,
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
            var payload = new
            {
                correlationId = operationId.ToString("D"),
                applicationId = ApplicationId,
                flowType,
                instanceId,
                startedAtUtc = occurredAt,
                observedAtUtc = now,
                currentStage = "Acknowledged",
                relatedBusinessId = businessId,
                runbook = "Cafeteria Services / POS transaction processing",
                stages = new object[]
                {
                    new { name = "POS", state = "Unknown", error = (string?)null },
                    new { name = "LocalStore", state = "Unknown", error = (string?)null },
                    new { name = "Services", state = "Completed", startedAtUtc = occurredAt, completedAtUtc = now, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null },
                    new { name = "Processing", state = "Completed", startedAtUtc = occurredAt, completedAtUtc = now, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null },
                    new { name = "Database", state = "Completed", startedAtUtc = occurredAt, completedAtUtc = now, warningAfterSeconds = 5, criticalAfterSeconds = 30, error = (string?)null },
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
            request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", operationId.ToString("N"));

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
