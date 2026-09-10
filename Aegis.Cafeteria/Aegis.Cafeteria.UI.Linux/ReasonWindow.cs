using Avalonia.Controls;
using Avalonia.Layout;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class ReasonWindow : Window
{
    private readonly TextBox reason = new() { MinWidth = 360, MinHeight = 70, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    public ReasonWindow(string title, string prompt, string? defaultReason = null)
    {
        Title = title;
        this.reason.Text = defaultReason?.Trim() ?? string.Empty;
        Width = 480;
        Height = 250;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12 };
        root.Children.Add(new TextBlock { Text = prompt, FontWeight = Avalonia.Media.FontWeight.Bold });
        root.Children.Add(reason);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var ok = new Button { Content = "OK", MinWidth = 90 };
        cancel.Click += (_, _) => Close((string?)null);
        ok.Click += (_, _) =>
        {
            var value = reason.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) Close(value);
        };
        actions.Children.Add(cancel); actions.Children.Add(ok); root.Children.Add(actions);
        Content = root;
        Opened += (_, _) =>
        {
            this.reason.Focus();
            this.reason.SelectAll();
        };
    }
}
