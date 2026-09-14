namespace Aegis.Cafeteria.UI;

using System.Globalization;
using System.Reflection;

internal static class UiDisplayFormattingPolicy
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Red = Color.FromArgb(176, 52, 52);
    private static readonly Color AlternateRow = Color.FromArgb(248, 250, 252);
    private static readonly Color SelectedRow = Color.FromArgb(226, 236, 246);
    private static readonly Color GridLine = Color.FromArgb(226, 231, 236);
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
        ApplyCustomerDisplayGridTheme(grid);

        grid.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = grid.Columns[eventArgs.ColumnIndex];

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

            if (IsTimeColumn(column)
                && eventArgs.Value is string timeText
                && TryFormatTime(timeText, out string formattedTime))
            {
                eventArgs.Value = formattedTime;
                eventArgs.FormattingApplied = true;
                return;
            }

            if (!TryGetDecimal(eventArgs.Value, out decimal value) || value >= 0m)
            {
                return;
            }

            eventArgs.CellStyle.ForeColor = Red;
            string format = column.DefaultCellStyle.Format;
            if (string.IsNullOrWhiteSpace(format))
            {
                eventArgs.Value = $"({Math.Abs(value):#,##0.##})";
                eventArgs.FormattingApplied = true;
            }
        };

        ConfigureRefundSelectionFilter(grid);
    }

    private static void ApplyCustomerDisplayGridTheme(DataGridView grid)
    {
        grid.AllowUserToResizeRows = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 44;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 38;
        grid.GridColor = GridLine;

        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Navy;
        grid.DefaultCellStyle.SelectionBackColor = SelectedRow;
        grid.DefaultCellStyle.SelectionForeColor = Navy;
        grid.DefaultCellStyle.Padding = new Padding(8, 3, 8, 3);

        grid.AlternatingRowsDefaultCellStyle.BackColor = AlternateRow;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Navy;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 4, 8, 4);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
    }

    private static void ConfigureLabel(Label label)
    {
        void ApplyFormatting()
        {
            string text = label.Text.Trim();
            if (TryNormalizeNegativeText(text, out string normalizedNegative))
            {
                if (!string.Equals(label.Text, normalizedNegative, StringComparison.Ordinal))
                {
                    label.Text = normalizedNegative;
                }

                label.ForeColor = Red;
                return;
            }

            if (TryNormalizeDisplayedTime(text, out string normalizedTime)
                && !string.Equals(label.Text, normalizedTime, StringComparison.Ordinal))
            {
                label.Text = normalizedTime;
            }
        }

        label.TextChanged += (_, _) => ApplyFormatting();
        ApplyFormatting();
    }

    private static bool TryNormalizeDisplayedTime(string text, out string normalized)
    {
        normalized = text;
        const string updatedPrefix = "Updated ";
        if (!text.StartsWith(updatedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string timeText = text[updatedPrefix.Length..].Trim();
        if (!TryFormatTime(timeText, out string formattedTime))
        {
            return false;
        }

        normalized = updatedPrefix + formattedTime;
        return true;
    }

    private static bool IsTimeColumn(DataGridViewColumn column)
    {
        return ContainsTimeToken(column.Name)
            || ContainsTimeToken(column.DataPropertyName)
            || ContainsTimeToken(column.HeaderText);
    }

    private static bool ContainsTimeToken(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains("time", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFormatTime(string value, out string formatted)
    {
        string[] acceptedFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "h:mm tt", "h:mm:ss tt"];
        if (TimeOnly.TryParseExact(value.Trim(), acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out TimeOnly time))
        {
            formatted = time.Second == 0
                ? time.ToString("h:mm tt", CultureInfo.CurrentCulture)
                : time.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            return true;
        }

        formatted = value;
        return false;
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
        Control? owningPanel = grid.Parent;
        if (owningPanel is null)
        {
            return false;
        }

        return DescendantsAndSelf(owningPanel)
            .OfType<Button>()
            .Any(button => string.Equals(button.Text, "Refund Selected", StringComparison.OrdinalIgnoreCase));
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
