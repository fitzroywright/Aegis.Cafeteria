namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Development-only secret provider backed by the DevelopmentSecrets configuration section.
/// Production startup must not select this provider.
/// </summary>
public sealed class ConfigurationSecretProvider(IConfiguration configuration) : IApplicationSecretProvider
{
    public Task<string?> GetSecretAsync(string logicalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
            throw new ArgumentException("A logical secret name is required.", nameof(logicalName));

        var value = configuration[$"DevelopmentSecrets:{logicalName}"];
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
