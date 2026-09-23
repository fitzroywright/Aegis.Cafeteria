using System.Text.Json.Nodes;

namespace Aegis.Cafeteria.Services.Engineering.Diagnostics;

public static class CafeteriaDiagnosticLevelContract
{
    public const string RequestCredentialSecretName =
        "diagnostics/machine/Aegis.Cafeteria.Services";

    public const string RunPath =
        "/api/engineering/diagnostics/diagnostic-level/run";

    public const string RunsPath =
        "/api/engineering/diagnostics/diagnostic-level/runs";

    public const string AuthenticationScheme =
        "DiagnosticLevel-HMAC-SHA256";

    public static JsonObject BuildCapability(
        IConfiguration configuration,
        string? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string secretName =
            configuration["Aegis:Diagnostics:RequestCredentialSecretName"]?.Trim()
            ?? RequestCredentialSecretName;

        return new JsonObject
        {
            ["baseUrl"] = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim(),
            ["healthPath"] = "/health",
            ["diagnosticsRunPath"] = RunPath,
            ["diagnosticsRunsPath"] = RunsPath,
            ["telemetryPath"] = "/api/engineering/diagnostics/telemetry",
            ["supportsRemoteDiagnostics"] = true,
            ["supportsOperationalTelemetry"] = true,
            ["authenticationScheme"] = AuthenticationScheme,
            ["secretName"] = secretName,
            ["supportedLevels"] = new JsonArray(5, 4, 3, 2, 1)
        };
    }
}
