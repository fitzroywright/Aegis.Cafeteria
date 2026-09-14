namespace Aegis.Cafeteria.Services;

using System.Security.Claims;

public static class CafeteriaRoles
{
    public const string Cashier = "Cashier";
    public const string Supervisor = "Supervisor";
    public const string Manager = "Manager";
    public const string Administrator = "Administrator";

    public static readonly string[] All = [Cashier, Supervisor, Manager, Administrator];
    public static readonly string[] ManagementRoles = [Supervisor, Manager, Administrator];
    public static readonly string[] ManagerRoles = [Manager, Administrator];
}

public static class CafeteriaPermissions
{
    public const string UsePos = "pos.use";
    public const string CompleteSale = "sale.complete";
    public const string PayrollDeduction = "payroll-deduction.create";
    public const string Refund = "refund.create";
    public const string VoidTransaction = "transaction.void";
    public const string ViewReports = "reports.view";
    public const string ClosePeriod = "period.close";
    public const string AdditionalCredit = "additional-credit.manage";
    public const string Stipend = "stipend.manage";
    public const string ViewAudit = "audit.view";
    public const string ChangeSettings = "settings.manage";
    public const string ManageUsers = "users.manage";
    public const string ManageCatalog = "catalog.manage";
    public const string ManageNotifications = "notifications.manage";
    public const string ManageOperations = "operations.manage";
    public const string RunDiagnostics = "diagnostics.run";
    public const string ManageBackups = "backups.manage";

    public static readonly string[] All =
    [
        UsePos, CompleteSale, PayrollDeduction, Refund, VoidTransaction,
        ViewReports, ClosePeriod, AdditionalCredit, Stipend, ViewAudit,
        ChangeSettings, ManageUsers, ManageCatalog, ManageNotifications,
        ManageOperations, RunDiagnostics, ManageBackups
    ];

    public static IReadOnlyList<string> ForRole(string role)
    {
        return role switch
        {
            CafeteriaRoles.Administrator => All,
            CafeteriaRoles.Manager =>
            [
                UsePos, CompleteSale, PayrollDeduction, Refund, VoidTransaction,
                ViewReports, ClosePeriod, AdditionalCredit, Stipend, ViewAudit,
                ManageCatalog, ManageNotifications, ManageOperations, RunDiagnostics,
                ManageBackups
            ],
            CafeteriaRoles.Supervisor =>
            [
                UsePos, CompleteSale, PayrollDeduction, Refund, VoidTransaction,
                ViewReports, ClosePeriod, RunDiagnostics
            ],
            CafeteriaRoles.Cashier => [UsePos, CompleteSale, PayrollDeduction],
            _ => []
        };
    }

    public static bool HasPermission(ClaimsPrincipal principal, string permission)
    {
        string? role = principal.FindFirst(ClaimTypes.Role)?.Value;
        return role is not null && ForRole(role).Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}

public static class CafeteriaPolicies
{
    public const string Pos = "CafeteriaPos";
    public const string Management = "CafeteriaManagement";
    public const string Supervisor = "CafeteriaSupervisor";
    public const string Manager = "CafeteriaManager";
    public const string Administrator = "CafeteriaAdministrator";
    public const string Audit = "CafeteriaAudit";

    public static string ForPermission(string permission) => $"CafeteriaPermission:{permission}";
}

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

    private static HashSet<string> ReadUsers(IConfiguration configuration, string path)
    {
        return configuration
            .GetSection(path)
            .GetChildren()
            .Select(child => Normalize(child.Value ?? string.Empty))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

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
