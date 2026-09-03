namespace Aegis.Cafeteria.UI;

using System.Globalization;
using System.Reflection;

internal static class UiDisplayFormattingPolicy
{
    private static readonly Color Red = Color.FromArgb(176, 52, 52);
    private static readonly HashSet<DataGridView> ConfiguredGrids = [];
    private static readonly HashSet<Label> ConfiguredLabels = [];

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

            if (control is Label label && ConfiguredLabels.Add(label))
            {
                ConfigureLabel(label);
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

        ConfigureRefundSelectionFilter(grid);
    }

    private static void ConfigureLabel(Label label)
    {
        void ApplyNegativeStyle()
        {
            string text = label.Text.Trim();
            if (TryNormalizeNegativeText(text, out string normalized))
            {
                if (!string.Equals(label.Text, normalized, StringComparison.Ordinal))
                {
                    label.Text = normalized;
                }

                label.ForeColor = Red;
            }
        }

        label.TextChanged += (_, _) => ApplyNegativeStyle();
        ApplyNegativeStyle();
    }

    private static bool TryNormalizeNegativeText(string text, out string normalized)
    {
        normalized = text;
        if (!text.Any(char.IsDigit))
        {
            return false;
        }

        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            return true;
        }

        if (text.StartsWith("-$", StringComparison.Ordinal))
        {
            normalized = $"(${text[2..]})";
            return true;
        }

        if (text.StartsWith("$-", StringComparison.Ordinal))
        {
            normalized = $"(${text[2..]})";
            return true;
        }

        if (text.StartsWith("-", StringComparison.Ordinal))
        {
            normalized = $"({text[1..]})";
            return true;
        }

        return false;
    }

    private static void ConfigureRefundSelectionFilter(DataGridView grid)
    {
        if (!IsRefundSelectionGrid(grid) || grid.DataSource is not BindingSource source)
        {
            return;
        }

        BindingSource filtered = new();
        grid.DataSource = filtered;

        void RefreshFilteredRows()
        {
            List<RefundSelectionTransactionRow> rows = [];
            foreach (object? item in source.List)
            {
                RefundSelectionTransactionRow? row = MapRefundSelectionRow(item);
                if (row is not null && !string.Equals(row.Kind, "Refund", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(row);
                }
            }

            filtered.DataSource = rows;
        }

        source.DataSourceChanged += (_, _) => RefreshFilteredRows();
        source.ListChanged += (_, _) => RefreshFilteredRows();
        RefreshFilteredRows();
    }

    private static RefundSelectionTransactionRow? MapRefundSelectionRow(object? source)
    {
        if (source is null)
        {
            return null;
        }

        Type type = source.GetType();
        PropertyInfo? dateProperty = type.GetProperty("Date");
        PropertyInfo? kindProperty = type.GetProperty("Kind");
        PropertyInfo? descriptionProperty = type.GetProperty("Description");
        PropertyInfo? amountProperty = type.GetProperty("Amount");
        PropertyInfo? accountProperty = type.GetProperty("Account");
        PropertyInfo? cashProperty = type.GetProperty("Cash");
        PropertyInfo? referenceProperty = type.GetProperty("ReferenceId");

        if (dateProperty?.GetValue(source) is not DateTime date
            || kindProperty?.GetValue(source) is not string kind
            || descriptionProperty?.GetValue(source) is not string description
            || referenceProperty?.GetValue(source) is not Guid referenceId)
        {
            return null;
        }

        decimal amount = Convert.ToDecimal(amountProperty?.GetValue(source) ?? 0m, CultureInfo.InvariantCulture);
        decimal account = Convert.ToDecimal(accountProperty?.GetValue(source) ?? 0m, CultureInfo.InvariantCulture);
        decimal cash = Convert.ToDecimal(cashProperty?.GetValue(source) ?? 0m, CultureInfo.InvariantCulture);
        return new RefundSelectionTransactionRow(date, kind, description, amount, account, cash, referenceId);
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

    private sealed record RefundSelectionTransactionRow
    (
          DateTime Date
        , string Kind
        , string Description
        , decimal Amount
        , decimal Account
        , decimal Cash
        , Guid ReferenceId
    );
}
