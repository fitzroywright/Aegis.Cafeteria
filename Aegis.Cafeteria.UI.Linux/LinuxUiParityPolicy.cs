namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Presentation;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

internal static class LinuxUiParityPolicy
{
    private static readonly IBrush Navy = new SolidColorBrush(Color.Parse("#182D44"));
    private static readonly IBrush GridLine = new SolidColorBrush(Color.Parse("#E2E7EC"));
    private static readonly IBrush White = Brushes.White;
    private static readonly HashSet<Window> ConfiguredWindows = [];

    public static void ConfigureApplicationCulture()
    {
        CafeteriaPresentationCulture.ConfigureCurrentCulture();
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
