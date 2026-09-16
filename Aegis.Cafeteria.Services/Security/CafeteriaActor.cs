namespace Aegis.Cafeteria.Services;

internal static class CafeteriaActor
{
    internal static string Require(HttpContext http) => http.User.Identity?.Name ?? "UNKNOWN";
}