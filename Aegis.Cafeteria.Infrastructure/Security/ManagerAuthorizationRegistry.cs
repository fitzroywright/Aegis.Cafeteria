namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Configuration;
using System.Collections.Concurrent;
using Common.Security.Abstractions;
using Common.Security.Services;

public sealed class ManagerAuthorizationRegistry(
    IOperatorSession operatorSession,
    OperatorSecurityOptions options,
    IAccountStore accounts) : IManagerAuthorizationRegistry
{
    private readonly ConcurrentDictionary<string, (OperatorIdentity Manager, DateTimeOffset Expires)> grants = new(StringComparer.OrdinalIgnoreCase);

    public void Grant(OperatorIdentity manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (!manager.IsManager || !new AccountProvisioningService(accounts).IsCurrentSession(manager.AccountId, manager.SessionVersion))
            throw new UnauthorizedAccessException("The authenticated user is not a cafeteria manager.");

        grants[Normalize(manager.UserId)] = (manager, DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, options.ManagerAuthorizationMinutes)));
    }

    public bool IsAuthorized(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return false;
        var normalized = Normalize(userId);
        var current = operatorSession.Current;
        if (current is not null && current.IsManager &&
            string.Equals(Normalize(current.UserId), normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!grants.TryGetValue(normalized, out var grant)) return false;
        if (grant.Expires >= DateTimeOffset.UtcNow &&
            new AccountProvisioningService(accounts).IsCurrentSession(grant.Manager.AccountId, grant.Manager.SessionVersion)) return true;
        grants.TryRemove(normalized, out _);
        return false;
    }

    private static string Normalize(string value)
    {
        var trimmed = value.Trim();
        var slash = trimmed.LastIndexOf('\\');
        if (slash >= 0) trimmed = trimmed[(slash + 1)..];
        var at = trimmed.IndexOf('@');
        if (at > 0) trimmed = trimmed[..at];
        return trimmed.ToLowerInvariant();
    }
}
