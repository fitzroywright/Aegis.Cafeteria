namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed record CafeteriaRuntimeOptions(
    CafeteriaPersistenceOptions Persistence,
    CafeteriaLoggingOptions Logging,
    BitwardenSecretsManagerOptions SecretsManager,
        CafeteriaHealthOptions Health,
    CafeteriaMessagingOptions Messaging,
    CafeteriaServicesOptions Services);
