namespace Aegis.Cafeteria.Infrastructure.Secrets;

using Aegis.Cafeteria.Application.Abstractions;

/// <summary>
/// Compatibility guard for the obsolete one-shot registration path that still exists in the
/// Services startup file. Configuration registration is owned by the current
/// Aegis.Cafeteria.Services.ConfigurationContractPublisher using Common.Registration.
///
/// All non-registration secret lookups continue to flow to the legacy provider unchanged.
/// The registration credential is deliberately withheld here so the obsolete publisher cannot
/// create a second contract with the old Cafeteria.Services application identity.
/// </summary>
internal sealed class LegacyRegistrationSuppressingSecretProvider(IApplicationSecretProvider inner)
    : IApplicationSecretProvider
{
    public Task<string?> GetSecretAsync(string logicalName, CancellationToken cancellationToken = default)
    {
        if (string.Equals(logicalName, ApplicationSecretNames.ConfigurationRegistrationKey, StringComparison.Ordinal))
        {
            return Task.FromResult<string?>(null);
        }

        return inner.GetSecretAsync(logicalName, cancellationToken);
    }
}
