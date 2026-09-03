namespace Aegis.Cafeteria.UI.Linux;

using System.Globalization;
using System.Reflection;
using Aegis.Cafeteria.Application.Reporting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class LinuxUiParityPolicy
{
    private const string MainFinancialSummaryName = "MainCustomerFinancialSummary";
    private static readonly IBrush Navy = new SolidColorBrush(Color.Parse("#182D44"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#687584"));
    private static readonly IBrush GridLine = new SolidColorBrush(Color.Parse("#E2E7EC"));
    private static readonly IBrush White = Brushes.White;
    private static readonly HashSet<Window> ConfiguredWindows = [];

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

    public static void Apply(Window window)
    {
        if (!ConfiguredWindows.Add(window))
        {
            return;
        }

        ApplyToVisualTree(window);
        window.Opened += (_, _) => ApplyToVisualTree(window);

        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        timer.Tick += (_, _) => ApplyToVisualTree(window);
        window.Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    private static void ApplyToVisualTree(Control root)
    {
        if (root is MainWindow mainWindow)
        {
            NormalizeMainCustomerFinancialLayout(mainWindow);
            RefreshCurrentCreditSalesValue(mainWindow);
        }

        foreach (Control control in DescendantsAndSelf(root))
        {
            if (control is ListBox listBox)
            {
                ApplyTableTheme(listBox);
            }

            if (control is TextBlock textBlock)
            {
                NormalizeTimeText(textBlock);
            }
        }
    }

    private static void NormalizeMainCustomerFinancialLayout(MainWindow mainWindow)
    {
        Grid? existingSummary = DescendantsAndSelf(mainWindow)
            .OfType<Grid>()
            .FirstOrDefault(grid => string.Equals(grid.Name, MainFinancialSummaryName, StringComparison.Ordinal));
        if (existingSummary is not null)
        {
            return;
        }

        Grid? finances = DescendantsAndSelf(mainWindow)
            .OfType<Grid>()
            .FirstOrDefault(IsExistingFinancialGrid);
        if (finances is null)
        {
            return;
        }

        TextBlock? available = GetPrivateTextBlock(mainWindow, "available");
        TextBlock? carriedDebt = GetPrivateTextBlock(mainWindow, "carriedDebt");
        TextBlock? creditSales = GetPrivateTextBlock(mainWindow, "customerLiability");
        TextBlock? employeePayments = GetPrivateTextBlock(mainWindow, "customerPaymentsValue");
        TextBlock? outstandingBalance = GetPrivateTextBlock(mainWindow, "employeeAmountDue");

        if (available is null
            || carriedDebt is null
            || creditSales is null
            || employeePayments is null
            || outstandingBalance is null)
        {
            return;
        }

        finances.Children.Clear();
        finances.RowDefinitions.Clear();
        finances.Name = MainFinancialSummaryName;

        AddFinancialRow(finances, 0, "Available to Spend", available);
        AddFinancialRow(finances, 1, "Debt Brought Forward", carriedDebt);
        AddFinancialRow(finances, 2, "Credit Sales This Month", creditSales);
        AddFinancialRow(finances, 3, "Employee Payments", employeePayments);
        AddFinancialRow(finances, 4, "Outstanding Balance", outstandingBalance);
    }

    private static bool IsExistingFinancialGrid(Grid grid)
    {
        IReadOnlyList<string> captions = grid.Children
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text ?? string.Empty)
            .ToArray();

        return captions.Any(text => string.Equals(text, "Debt Brought Forward", StringComparison.OrdinalIgnoreCase))
            && captions.Any(text => string.Equals(text, "Customer Payments", StringComparison.OrdinalIgnoreCase))
            && captions.Any(text => string.Equals(text, "Employee Amount Due", StringComparison.OrdinalIgnoreCase));
    }

    private static void RefreshCurrentCreditSalesValue(MainWindow mainWindow)
    {
        TextBlock? creditSales = GetPrivateTextBlock(mainWindow, "customerLiability");
        if (creditSales is null)
        {
            return;
        }

        CustomerAccountView? account = GetPrivateField<CustomerAccountView>(mainWindow, "currentCustomer");
        if (account is null)
        {
            creditSales.Text = "$0.00";
            return;
        }

        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit)
        {
            creditSales.Text = "—";
            return;
        }

        creditSales.Text = account.FinancialStatus.CurrentCreditSaleDebt.ToString("C2");
    }

    private static TextBlock? GetPrivateTextBlock(MainWindow mainWindow, string fieldName)
    {
        return GetPrivateField<TextBlock>(mainWindow, fieldName);
    }

    private static T? GetPrivateField<T>(MainWindow mainWindow, string fieldName)
        where T : class
    {
        FieldInfo? field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(mainWindow) as T;
    }

    private static void AddFinancialRow(Grid grid, int row, string caption, Control value)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock label = new()
        {
            Text = caption,
            Foreground = Muted,
            Margin = new Thickness(0, 3)
        };
        Grid.SetRow(label, row);
        grid.Children.Add(label);

        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }

    private static void ApplyTableTheme(ListBox listBox)
    {
        listBox.Background = White;
        listBox.BorderBrush = GridLine;
        listBox.BorderThickness = new Thickness(0, 1, 0, 1);
        listBox.Foreground = Navy;

        if (listBox.ItemTemplate is null)
        {
            listBox.ItemTemplate = new FuncDataTemplate<object?>
            (
                (item, _) => new Border
                {
                    BorderBrush = GridLine,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 6),
                    Child = new TextBlock
                    {
                        Text = FormatDisplayText(item?.ToString() ?? string.Empty),
                        Foreground = Navy,
                        TextWrapping = TextWrapping.NoWrap
                    }
                }
            );
        }
    }

    private static void NormalizeTimeText(TextBlock textBlock)
    {
        string value = textBlock.Text ?? string.Empty;
        const string updatedPrefix = "Updated ";
        if (!value.StartsWith(updatedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string timeText = value[updatedPrefix.Length..].Trim();
        if (!TryFormatTime(timeText, out string formatted))
        {
            return;
        }

        string normalized = updatedPrefix + formatted;
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            textBlock.Text = normalized;
        }
    }

    private static string FormatDisplayText(string value)
    {
        if (value.Length >= 5
            && TryFormatTime(value[..5], out string formatted)
            && value.Length > 5
            && char.IsWhiteSpace(value[5]))
        {
            return formatted + value[5..];
        }

        return value;
    }

    private static bool TryFormatTime(string value, out string formatted)
    {
        string[] formats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "h:mm tt", "h:mm:ss tt"];
        if (TimeOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out TimeOnly time))
        {
            formatted = time.Second == 0
                ? time.ToString("h:mm tt", CultureInfo.CurrentCulture)
                : time.ToString("h:mm:ss tt", CultureInfo.CurrentCulture);
            return true;
        }

        formatted = value;
        return false;
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.GetVisualChildren().OfType<Control>())
        {
            foreach (Control descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }
}
