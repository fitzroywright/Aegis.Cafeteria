namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed record CafeteriaRuntimeOptions(
    CafeteriaPersistenceOptions Persistence,
    CafeteriaLoggingOptions Logging,
    BitwardenSecretsManagerOptions SecretsManager,
    FfpIntegrationOptions FfpIntegration,
    CafeteriaHealthOptions Health,
    CafeteriaMessagingOptions Messaging,
    CafeteriaServicesOptions Services);
