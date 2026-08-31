using Avalonia.Controls;
using Avalonia.Layout;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class ConfirmWindow : Window
{
    public ConfirmWindow(string title, string message)
    {
        Title = title; Width = 460; Height = 210; CanResize = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Avalonia.Thickness(26), Spacing = 16 };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 15 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var no = new Button { Content = "No", MinWidth = 90 };
        var yes = new Button { Content = "Yes", MinWidth = 90 };
        no.Click += (_, _) => Close(false); yes.Click += (_, _) => Close(true);
        buttons.Children.Add(no); buttons.Children.Add(yes); root.Children.Add(buttons); Content = root;
    }
}
