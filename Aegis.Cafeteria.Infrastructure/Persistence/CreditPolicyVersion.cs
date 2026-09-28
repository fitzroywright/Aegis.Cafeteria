namespace Aegis.Cafeteria.Infrastructure.Persistence;

internal static class CreditPolicyVersion
{
    internal static bool ShouldApply(DateTimeOffset? incoming, DateTimeOffset? current)
        => !current.HasValue || (incoming.HasValue && incoming.Value > current.Value);
}
