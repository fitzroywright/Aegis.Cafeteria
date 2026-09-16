namespace Aegis.Cafeteria.Services;

using System.Security.Claims;

public sealed class CafeteriaRoleResolver
{
    private readonly HashSet<string> administrators;
    private readonly HashSet<string> managers;
    private readonly HashSet<string> supervisors;
    private readonly string defaultManagementRole;

    public CafeteriaRoleResolver(IConfiguration configuration)
    {
        administrators = ReadUsers(configuration, "ManagementApi:RoleAssignments:Administrators");
        managers = ReadUsers(configuration, "ManagementApi:RoleAssignments:Managers");
        supervisors = ReadUsers(configuration, "ManagementApi:RoleAssignments:Supervisors");

        string configuredDefault = configuration["ManagementApi:DefaultManagementRole"] ?? CafeteriaRoles.Supervisor;
        defaultManagementRole = CafeteriaRoles.ManagementRoles.Contains(configuredDefault, StringComparer.OrdinalIgnoreCase)
            ? configuredDefault
            : CafeteriaRoles.Supervisor;
    }

    public string Resolve(string userName, bool isManagementAuthorized)
    {
        string normalized = Normalize(userName);
        if (administrators.Contains(normalized)) return CafeteriaRoles.Administrator;
        if (managers.Contains(normalized)) return CafeteriaRoles.Manager;
        if (supervisors.Contains(normalized)) return CafeteriaRoles.Supervisor;
        return isManagementAuthorized ? defaultManagementRole : CafeteriaRoles.Cashier;
    }

    public static bool HasRole(ClaimsPrincipal principal, params string[] roles)
    {
        string? role = principal.FindFirst(ClaimTypes.Role)?.Value;
        return role is not null && roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> ReadUsers(IConfiguration configuration, string path) => configuration
        .GetSection(path)
        .GetChildren()
        .Select(child => Normalize(child.Value ?? string.Empty))
        .Where(value => value.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string loginName)
    {
        string value = loginName.Trim();
        int slash = value.LastIndexOf('\\');
        if (slash >= 0 && slash < value.Length - 1) value = value[(slash + 1)..];
        int at = value.IndexOf('@');
        if (at > 0) value = value[..at];
        return value.ToLowerInvariant();
    }
}
