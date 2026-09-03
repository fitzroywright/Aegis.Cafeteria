namespace Aegis.Cafeteria.UI.Linux;

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;

internal static class LinuxUiParityPolicy
{
    private static readonly IBrush Navy = new SolidColorBrush(Color.Parse("#182D44"));
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
        NormalizeMainCustomerFinancialLayout(root);

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

    private static void NormalizeMainCustomerFinancialLayout(Control root)
    {
        foreach (Grid grid in DescendantsAndSelf(root).OfType<Grid>())
        {
            StackPanel? availableRow = grid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => HasCaption(panel, "Total Available to Spend") || HasCaption(panel, "Available to Spend"));

            StackPanel? debtRow = grid.Children
                .OfType<StackPanel>()
                .FirstOrDefault(panel => HasCaption(panel, "Debt Brought Forward"));

            if (availableRow is null || debtRow is null)
            {
                continue;
            }

            TextBlock? availableCaption = availableRow.Children
                .OfType<TextBlock>()
                .FirstOrDefault();
            if (availableCaption is not null)
            {
                availableCaption.Text = "Available to Spend";
            }

            int debtIndex = Grid.GetRow(debtRow);
            int availableIndex = Grid.GetRow(availableRow);
            if (availableIndex == debtIndex - 1)
            {
                return;
            }

            if (availableIndex < debtIndex)
            {
                foreach (Control child in grid.Children.OfType<Control>())
                {
                    int row = Grid.GetRow(child);
                    if (!ReferenceEquals(child, availableRow) && row > availableIndex && row < debtIndex)
                    {
                        Grid.SetRow(child, row - 1);
                    }
                }

                Grid.SetRow(availableRow, debtIndex - 1);
            }
            else
            {
                foreach (Control child in grid.Children.OfType<Control>())
                {
                    int row = Grid.GetRow(child);
                    if (!ReferenceEquals(child, availableRow) && row >= debtIndex && row < availableIndex)
                    {
                        Grid.SetRow(child, row + 1);
                    }
                }

                Grid.SetRow(availableRow, debtIndex);
                Grid.SetRow(debtRow, debtIndex + 1);
            }

            return;
        }
    }

    private static bool HasCaption(StackPanel panel, string caption)
    {
        return panel.Children
            .OfType<TextBlock>()
            .Any(textBlock => string.Equals(textBlock.Text, caption, StringComparison.OrdinalIgnoreCase));
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
