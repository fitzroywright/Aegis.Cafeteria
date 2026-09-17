namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;
using Common.Secrets;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Adapts the provider-neutral Common.Secrets contract to the Cafeteria application abstraction.
/// Provider selection and provider-specific configuration remain entirely inside Common.Secrets.
/// </summary>
public static class ApplicationSecretProviderFactory
{
    public static IApplicationSecretProvider Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new CommonSecretsApplicationSecretProvider(CommonSecretProviderFactory.Create(configuration));
    }

    public static string Describe() => "Common.Secrets";

    private sealed class CommonSecretsApplicationSecretProvider(ISecretProvider provider) : IApplicationSecretProvider
    {
        public Task<string?> GetSecretAsync(
            string logicalName,
            CancellationToken cancellationToken = default)
            => provider.GetAsync(logicalName, cancellationToken);
    }
}
