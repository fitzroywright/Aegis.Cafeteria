namespace Aegis.Cafeteria.Application.Security;

public sealed record OperatorIdentity(
    string UserId,
    string DisplayName,
    bool IsManager,
    DateTimeOffset AuthenticatedAt);
