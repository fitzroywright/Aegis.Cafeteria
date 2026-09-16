namespace Aegis.Cafeteria.Services;

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
