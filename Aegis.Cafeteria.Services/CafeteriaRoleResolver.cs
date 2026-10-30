namespace Aegis.Cafeteria.Services;
using System.Security.Claims;

public static class CafeteriaRoleResolver
{
    // POS manager approval and management administration remain distinct business permissions.
    public static string? ResolveManagementRole(IConfiguration configuration,string accountId,bool approvedManager)
    {
        if(!approvedManager)return null;
        string? role=configuration["ManagementApi:AccountRoles:"+accountId];
        return role is CafeteriaRoles.Administrator or CafeteriaRoles.Manager or CafeteriaRoles.Supervisor ? role : CafeteriaRoles.Manager;
    }
    public static bool HasRole(ClaimsPrincipal principal, params string[] roles) => principal.FindAll(ClaimTypes.Role)
        .Any(claim=>roles.Contains(claim.Value,StringComparer.OrdinalIgnoreCase));
}
