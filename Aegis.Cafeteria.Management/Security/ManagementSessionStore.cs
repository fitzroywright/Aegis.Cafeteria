using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Aegis.Cafeteria.Management;

internal sealed class ManagementSessionStore(TimeSpan sessionLifetime)
{
    private readonly ConcurrentDictionary<string, ManagementSession> sessions = new(StringComparer.Ordinal);

    public string Create(ManagementAuthenticationResponse authentication)
    {
        string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(sessionLifetime);
        if (authentication.ExpiresAt > DateTimeOffset.UtcNow && authentication.ExpiresAt < expiresAt)
            expiresAt = authentication.ExpiresAt;

        sessions[sessionId] = new ManagementSession(authentication.Token, authentication.UserName, authentication.DisplayName, expiresAt);
        return sessionId;
    }

    public bool TryGet(string sessionId, out ManagementSession? session)
    {
        if (!sessions.TryGetValue(sessionId, out session)) return false;
        if (session.ExpiresAt > DateTimeOffset.UtcNow) return true;

        sessions.TryRemove(sessionId, out _);
        session = null;
        return false;
    }

    public void Remove(string sessionId) => sessions.TryRemove(sessionId, out _);
}
