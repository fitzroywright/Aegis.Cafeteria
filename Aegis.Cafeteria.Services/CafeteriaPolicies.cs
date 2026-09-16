namespace Aegis.Cafeteria.Services;

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
