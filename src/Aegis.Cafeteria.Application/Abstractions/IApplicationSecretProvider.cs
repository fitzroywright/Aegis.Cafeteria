namespace Aegis.Cafeteria.Application.Abstractions;

/// <summary>
/// Resolves application secrets by logical name. Implementations must never log secret values.
/// </summary>
public interface IApplicationSecretProvider
{
    Task<string?> GetSecretAsync(string logicalName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Common helpers for all application secret providers.
/// Kept as extension methods so they are available whether the provider is referenced
/// through IApplicationSecretProvider or through its concrete implementation type.
/// </summary>
public static class ApplicationSecretProviderExtensions
{
    public static async Task<string> GetRequiredSecretAsync(
        this IApplicationSecretProvider provider,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return await provider.GetSecretAsync(logicalName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Required secret '{logicalName}' is not configured or could not be resolved.");
    }
}

public static class ApplicationSecretNames
{
    public const string FfpDatabaseConnectionString = "FfpDatabaseConnectionString";
    public const string MessagingBearerToken = "MessagingBearerToken";
    public const string CafeteriaServicesApiKey = "CafeteriaServicesApiKey";
}
