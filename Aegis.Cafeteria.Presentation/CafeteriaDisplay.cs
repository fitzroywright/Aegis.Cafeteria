namespace Aegis.Cafeteria.Presentation;

using System.Globalization;

public static class CafeteriaDisplay
{
    public static string Money(decimal value)
        => value.ToString("C2", CultureInfo.CurrentCulture);

    public static string MoneyWhole(decimal value)
        => value.ToString("C0", CultureInfo.CurrentCulture);

    public static string DateTime(DateTimeOffset value)
        => value.LocalDateTime.ToString("M/d/yyyy h:mm tt", CultureInfo.CurrentCulture);

    public static string Time(DateTimeOffset value)
        => value.LocalDateTime.ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string Time(DateTime value)
        => value.ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string Updated(DateTime value)
        => $"Updated {Time(value)}";

    public static bool TryFormatTime(string value, out string formatted)
    {
        string[] formats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "h:mm tt", "h:mm:ss tt"];
        if (TimeOnly.TryParseExact
            (
                  value.Trim()
                , formats
                , CultureInfo.InvariantCulture
                , DateTimeStyles.AllowWhiteSpaces
                , out TimeOnly time
            ))
        {
            formatted = time.Second == 0
                ? time.ToString("h:mm tt", CultureInfo.CurrentCulture)
                : time.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            return true;
        }

        formatted = value;
        return false;
    }

    public static string NegativeNumber(decimal value, string format = "#,##0.##")
        => value < 0m
            ? $"({Math.Abs(value).ToString(format, CultureInfo.CurrentCulture)})"
            : value.ToString(format, CultureInfo.CurrentCulture);

    public static string NegativeMoney(decimal value)
        => value < 0m
            ? $"({Math.Abs(value).ToString("C2", CultureInfo.CurrentCulture)})"
            : Money(value);
}

public static class CafeteriaPresentationCulture
{
    public static void ConfigureCurrentCulture()
    {
        CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
        culture.NumberFormat.CurrencyNegativePattern = 0;
        culture.NumberFormat.NumberNegativePattern = 0;
        culture.DateTimeFormat.ShortTimePattern = "h:mm tt";
        culture.DateTimeFormat.LongTimePattern = "h:mm:ss tt";
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
