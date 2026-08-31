namespace Aegis.Cafeteria.Infrastructure.Configuration;

/// <summary>
/// Non-secret configuration required to resolve application secrets from Bitwarden Secrets Manager.
/// Secret values never belong in this object; only their Bitwarden secret IDs do.
/// </summary>
public sealed class BitwardenSecretsManagerOptions
{
    public string CliPath { get; init; } = "bws";
    public string AccessTokenEnvironmentVariable { get; init; } = "BWS_ACCESS_TOKEN";
    public string? ServerUrl { get; init; }
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public IReadOnlyDictionary<string, string> SecretIds { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
