namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Selects the legacy application secret source. Configuration registration is no longer owned
/// by this path; the current Cafeteria Services publisher uses Common.Registration and Common.Secrets.
/// </summary>
public static class ApplicationSecretProviderFactory
{
    public static IApplicationSecretProvider Create(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        IApplicationSecretProvider provider = environment.IsDevelopment()
            ? new ConfigurationSecretProvider(configuration)
            : new BwsSecretsManagerProvider(CafeteriaRuntimeOptionsResolver.ReadSecretsManagerOptions(configuration));

        return new LegacyRegistrationSuppressingSecretProvider(provider);
    }

    public static string Describe(IHostEnvironment environment)
        => environment.IsDevelopment()
            ? "Development configuration"
            : "Bitwarden Secrets Manager";
}
