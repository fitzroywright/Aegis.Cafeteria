namespace Aegis.Cafeteria.Application.Integration;

public static class LunchItemCode
{
    public static bool IsReserved(string? code)
        => code?.Trim().StartsWith("FFP", StringComparison.OrdinalIgnoreCase) == true;
}
