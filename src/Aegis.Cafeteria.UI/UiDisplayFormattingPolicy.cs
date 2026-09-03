namespace Aegis.Cafeteria.UI;

using System.Globalization;

internal static class UiDisplayFormattingPolicy
{
    private static readonly Color Red = Color.FromArgb(176, 52, 52);
    private static readonly HashSet<DataGridView> ConfiguredGrids = [];

    public static void ConfigureApplicationCulture()
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

    public static void ApplyToOpenForms()
    {
        foreach (Form form in System.Windows.Forms.Application.OpenForms)
        {
            ApplyTo(form);
        }
    }

    private static void ApplyTo(Control root)
    {
        foreach (Control control in DescendantsAndSelf(root))
        {
            if (control is DataGridView grid && ConfiguredGrids.Add(grid))
            {
                ConfigureGrid(grid);
            }
        }
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.Value is DateTime dateTime)
            {
                eventArgs.Value = dateTime.ToString("M/d/yyyy h:mm tt");
                eventArgs.FormattingApplied = true;
                return;
            }

            if (eventArgs.Value is DateTimeOffset dateTimeOffset)
            {
                eventArgs.Value = dateTimeOffset.LocalDateTime.ToString("M/d/yyyy h:mm tt");
                eventArgs.FormattingApplied = true;
                return;
            }

            if (!TryGetDecimal(eventArgs.Value, out decimal value) || value >= 0m)
            {
                return;
            }

            eventArgs.CellStyle.ForeColor = Red;
            string format = grid.Columns[eventArgs.ColumnIndex].DefaultCellStyle.Format;
            if (string.IsNullOrWhiteSpace(format))
            {
                eventArgs.Value = $"({Math.Abs(value):#,##0.##})";
                eventArgs.FormattingApplied = true;
            }
        };

        grid.DataBindingComplete += (_, _) => HideRefundRowsFromRefundSelectionGrid(grid);
    }

    private static void HideRefundRowsFromRefundSelectionGrid(DataGridView grid)
    {
        if (!IsRefundSelectionGrid(grid))
        {
            return;
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            object? boundItem = row.DataBoundItem;
            string? kind = boundItem?.GetType().GetProperty("Kind")?.GetValue(boundItem)?.ToString();
            if (string.Equals(kind, "Refund", StringComparison.OrdinalIgnoreCase))
            {
                row.Visible = false;
            }
        }
    }

    private static bool IsRefundSelectionGrid(DataGridView grid)
    {
        Control? current = grid.Parent;
        while (current is not null)
        {
            if (DescendantsAndSelf(current)
                .OfType<Button>()
                .Any(button => string.Equals(button.Text, "Refund Selected", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool TryGetDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal decimalValue:
                number = decimalValue;
                return true;
            case double doubleValue:
                number = Convert.ToDecimal(doubleValue);
                return true;
            case float floatValue:
                number = Convert.ToDecimal(floatValue);
                return true;
            case long longValue:
                number = longValue;
                return true;
            case int intValue:
                number = intValue;
                return true;
            case short shortValue:
                number = shortValue;
                return true;
            default:
                number = 0m;
                return false;
        }
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (Control descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }
}
