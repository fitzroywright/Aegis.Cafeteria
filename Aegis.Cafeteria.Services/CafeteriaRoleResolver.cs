namespace Aegis.Cafeteria.Services;
using System.Security.Claims;

public static class CafeteriaRoleResolver
{
    public static bool HasRole(ClaimsPrincipal principal, params string[] roles) => principal.FindAll(ClaimTypes.Role)
        .Any(claim=>roles.Contains(claim.Value,StringComparer.OrdinalIgnoreCase));
}
