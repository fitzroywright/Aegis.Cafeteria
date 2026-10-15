using Common.Diagnostics;
using Aegis.Engineering.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Common.Registration;

namespace Aegis.Cafeteria.Services;

public sealed class OperationsTelemetryPublisher(
    IConfiguration configuration,
    IHostEnvironment environment,
    CafeteriaEngineeringDiagnosticService diagnostics,
    ILogger<OperationsTelemetryPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";
    private const string DisplayName = "Aegis Cafeteria Services";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string operationsUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Operations,
            logger,
            configurationKey: "Aegis:Operations:Url");

        string instanceId = configuration["Service:Identity"]?.Trim() ?? Environment.MachineName;
        string identityFile = configuration["Aegis:Registration:IdentityFile"]
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aegis", "Cafeteria.Services", "registration-identity.json")
                : "/var/lib/aegis/cafeteria-services/registration-identity.json");

        var identityStore = new FileRegistrationIdentityStore(identityFile);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RegistrationIdentityDocument identity =
                    await identityStore.LoadOrCreateAsync(ApplicationId, instanceId, stoppingToken);

                if (string.IsNullOrWhiteSpace(identity.Credential))
                {
                    logger.LogDebug("Operations telemetry not published because the application registration has no durable credential yet.");
                }
                else
                {
                    EngineeringDiagnosticRun? diagnosticRun = null;
                    try
                    {
                        diagnosticRun = await diagnostics.RunAsync(
                            EngineeringDiagnosticLevel.Level4Analysis,
                            "Aegis.Operations",
                            null,
                            stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Cafeteria diagnostic publication could not collect current evidence for {ApplicationId}.", ApplicationId);
                    }

                    string operationalState = diagnosticRun is null
                        ? "Unknown"
                        : ToOperationalState(diagnosticRun.Status);

                    using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
                    using var request = new HttpRequestMessage(
                        HttpMethod.Post,
                        operationsUrl.TrimEnd('/') + "/api/operations/applications/observe")
                    {
                        Content = JsonContent.Create(new
                        {
                            applicationId = ApplicationId,
                            displayName = DisplayName,
                            instanceId,
                            environment = environment.EnvironmentName,
                            version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                            registrationStatus = "Registered",
                            state = operationalState,
                            observedAtUtc = DateTimeOffset.UtcNow,
                            configurationStatus = "Published",
                            dependencies = Array.Empty<string>(),
                            reason = diagnosticRun is null
                                ? "Process heartbeat received but diagnostic evidence could not be collected."
                                : $"Aggregate state is based on Common.Diagnostics Level 4 run {diagnosticRun.RunId}."
                        })
                    };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
                    ApplicationRuntimeSession.Apply(request);
                    request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", Guid.NewGuid().ToString("N"));

                    using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning(
                            "Operations heartbeat returned HTTP {StatusCode}; application remains operational.",
                            (int)response.StatusCode);
                    }

                    await PublishLifecycleHeartbeatAsync(
                        client,
                        operationsUrl,
                        identity,
                        instanceId,
                        operationalState,
                        stoppingToken);

                    await PublishFlowDefinitionAsync(
                        client,
                        operationsUrl,
                        identity,
                        stoppingToken);

                    if (diagnosticRun is not null)
                    {
                        await PublishDiagnosticsAsync(
                            client,
                            operationsUrl,
                            identity,
                            diagnosticRun,
                            stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Operations telemetry heartbeat failed for {ApplicationId}; application remains operational.", ApplicationId);
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private static async Task PublishFlowDefinitionAsync(
        HttpClient client,
        string operationsUrl,
        RegistrationIdentityDocument identity,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            operationsUrl.TrimEnd('/') + "/api/operations/flow-definitions")
        {
            Content = JsonContent.Create(new
            {
                applicationId = ApplicationId,
                flowType = "CafeteriaPurchase",
                displayName = "Cafeteria Purchase",
                stages = new object[]
                {
                new { name = "POS", parentStage = (string?)null, branch = (string?)null, warningAfterSeconds = 2, criticalAfterSeconds = 30 },
                new { name = "LocalStore", parentStage = "POS", branch = (string?)null, warningAfterSeconds = 2, criticalAfterSeconds = 30 },
                new { name = "Services", parentStage = "LocalStore", branch = (string?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30 },
                new { name = "Processing", parentStage = "Services", branch = (string?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30 },
                new { name = "Database", parentStage = "Processing", branch = (string?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30 },
                new { name = "Acknowledgement", parentStage = "Database", branch = (string?)null, warningAfterSeconds = 5, criticalAfterSeconds = 30 }
                },
                updatedAtUtc = DateTimeOffset.UtcNow
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
        request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
        request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
        ApplicationRuntimeSession.Apply(request);
        request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", Guid.NewGuid().ToString("N"));

        using HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Flow metadata is advisory to Operations and must never affect application availability.
        }
    }


    private static string ToOperationalState(EngineeringDiagnosticStatus status) =>
        status.ToString() switch
        {
            "Passed" => "Healthy",
            "Warning" => "Warning",
            "Failed" => "Failed",
            "InterventionRequired" => "Degraded",
            _ => "Unknown"
        };

    private static async Task PublishDiagnosticsAsync(
        HttpClient client,
        string operationsUrl,
        RegistrationIdentityDocument identity,
        EngineeringDiagnosticRun run,
        CancellationToken ct)
    {
        foreach (EngineeringDiagnosticCheckResult check in run.Checks)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                operationsUrl.TrimEnd('/') + "/api/operations/diagnostics/observe")
            {
                Content = JsonContent.Create(new
                {
                    diagnosticId = ApplicationId + ":" + check.CheckId,
                    applicationId = ApplicationId,
                    component = check.Name,
                    category = "Health",
                    state = ToOperationalState(check.Status),
                    summary = check.Summary,
                    observedAtUtc = run.CompletedAt,
                    detail = check.Evidence,
                    correlationId = run.RunId.ToString("D"),
                    runbook = "docs/RUNBOOK.md"
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
            request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
            request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
            request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
            ApplicationRuntimeSession.Apply(request);
            request.Headers.TryAddWithoutValidation("X-Aegis-Correlation-Id", run.RunId.ToString("N"));

            using HttpResponseMessage response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                break;
        }
    }

    private static async Task PublishLifecycleHeartbeatAsync(
        HttpClient client,
        string operationsUrl,
        RegistrationIdentityDocument identity,
        string instanceId,
        string operationalState,
        CancellationToken ct)
    {
        LifecycleEventOutcome outcome = operationalState switch
        {
            "Healthy" => LifecycleEventOutcome.Succeeded,
            "Warning" or "Degraded" => LifecycleEventOutcome.Warning,
            "Failed" or "Offline" => LifecycleEventOutcome.Failed,
            _ => LifecycleEventOutcome.Started
        };

        var sink = new HttpLifecycleEventSink(
            client,
            new HttpLifecycleEventSinkOptions(
                new Uri(operationsUrl.TrimEnd('/') + "/api/operations/lifecycle/events")),
            (request, item, _) =>
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.Credential);
                request.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", identity.ApplicationId);
                request.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", identity.InstanceId);
                request.Headers.TryAddWithoutValidation("X-Aegis-Installation-Id", identity.InstallationId);
                ApplicationRuntimeSession.Apply(request);
                return Task.CompletedTask;
            });

        var telemetry = new LifecycleTelemetry(sink);
        await telemetry.EmitAsync(
            LifecycleEvent.Create(
                ApplicationId,
                instanceId,
                "ApplicationLifecycle",
                operationalState,
                outcome,
                Guid.NewGuid().ToString("N"),
                code: "Heartbeat"),
            ct);
    }

}
