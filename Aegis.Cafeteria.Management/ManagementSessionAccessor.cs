using System.Security.Claims;

namespace Aegis.Cafeteria.Management;

internal static class ManagementSessionAccessor
{
    public const string SessionClaim = "aegis_management_session";

    public static bool TryGet(HttpContext http, ManagementSessionStore sessions, out ManagementSession? session)
    {
        string? sessionId = http.User.FindFirst(SessionClaim)?.Value;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            session = null;
            return false;
        }
        return sessions.TryGet(sessionId, out session);
    }
}
