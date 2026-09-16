namespace Aegis.Cafeteria.Services;

using System.Security.Claims;

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

    public static IReadOnlyList<string> ForRole(string role) => role switch
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

    public static bool HasPermission(ClaimsPrincipal principal, string permission)
    {
        string? role = principal.FindFirst(ClaimTypes.Role)?.Value;
        return role is not null && ForRole(role).Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}
