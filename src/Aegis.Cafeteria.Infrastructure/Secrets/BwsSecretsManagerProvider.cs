namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>
/// Bitwarden Secrets Manager adapter using the official bws CLI.
/// Values are cached in process memory after first successful resolution and are never persisted by this class.
/// </summary>
public sealed class BwsSecretsManagerProvider : IApplicationSecretProvider
{
    private readonly BitwardenSecretsManagerOptions options;
    private readonly IBwsCliExecutor executor;
    private readonly ConcurrentDictionary<string, string> memoryCache = new(StringComparer.OrdinalIgnoreCase);

    public BwsSecretsManagerProvider(BitwardenSecretsManagerOptions options)
        : this(options, new BwsCliExecutor()) { }

    internal BwsSecretsManagerProvider(BitwardenSecretsManagerOptions options, IBwsCliExecutor executor)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async Task<string?> GetSecretAsync(string logicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            throw new ArgumentException("A logical secret name is required.", nameof(logicalName));

        if (memoryCache.TryGetValue(logicalName, out var cached))
            return cached;

        if (!options.SecretIds.TryGetValue(logicalName, out var secretId) || string.IsNullOrWhiteSpace(secretId))
            return null;

        if (!Guid.TryParse(secretId, out _))
            throw new InvalidOperationException($"The Bitwarden secret ID configured for '{logicalName}' is not a valid UUID.");

        var token = Environment.GetEnvironmentVariable(options.AccessTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException(
                $"Bitwarden Secrets Manager bootstrap token is missing. Set the '{options.AccessTokenEnvironmentVariable}' environment variable.");

        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            args.Add("--server-url");
            args.Add(options.ServerUrl.Trim());
        }
        args.Add("--output");
        args.Add("json");
        args.Add("secret");
        args.Add("get");
        args.Add(secretId);

        var result = await executor.ExecuteAsync(options.CliPath, args, token, options.CommandTimeout, cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Bitwarden Secrets Manager could not resolve '{logicalName}' (bws exit code {result.ExitCode}). " +
                "Verify the machine account, secret ID, server URL, and access-token permissions.");

        var value = ParseSecretValue(result.StandardOutput, logicalName);
        memoryCache.TryAdd(logicalName, value);
        return value;
    }

    internal static string ParseSecretValue(string json, string logicalName)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Bitwarden returned no data for '{logicalName}'.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            if (root.GetArrayLength() != 1)
                throw new InvalidOperationException($"Bitwarden returned an unexpected number of objects for '{logicalName}'.");
            root = root[0];
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("value", out var valueElement) ||
            valueElement.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Bitwarden returned an invalid secret object for '{logicalName}'.");

        var value = valueElement.GetString();
        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Bitwarden secret '{logicalName}' has an empty value.");
        return value;
    }
}
