namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Selects the runtime secret source. Development may use local configuration;
/// all other environments require Bitwarden Secrets Manager.
/// </summary>
public static class ApplicationSecretProviderFactory
{
    public static IApplicationSecretProvider Create(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
            return new ConfigurationSecretProvider(configuration);

        var options = CafeteriaRuntimeOptionsResolver.ReadSecretsManagerOptions(configuration);
        return new BwsSecretsManagerProvider(options);
    }

    public static string Describe(IHostEnvironment environment)
        => environment.IsDevelopment()
            ? "Development configuration"
            : "Bitwarden Secrets Manager";
}
