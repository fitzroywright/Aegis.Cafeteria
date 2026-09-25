namespace Aegis.Cafeteria.UI.Linux;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

public sealed class DailySalesEmailWindow : Window
{
    private readonly TextBox recipients = new()
    {
          AcceptsReturn = true
        , TextWrapping = TextWrapping.Wrap
        , MinHeight = 110
        , MinWidth = 500
    };

    private readonly TextBlock status = new()
    {
          Foreground = Brushes.Firebrick
    };

    public DailySalesEmailWindow(string? defaultRecipient)
    {
        if (!string.IsNullOrWhiteSpace(defaultRecipient))
        {
            this.recipients.Text = defaultRecipient.Trim();
        }

        Title = "Email Daily Sales Report";
        Width = 620;
        Height = 340;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        StackPanel root = new() { Margin = new Avalonia.Thickness(24), Spacing = 10 };
        root.Children.Add(new TextBlock
        {
              Text = "Email Daily Sales Report"
            , FontSize = 20
            , FontWeight = FontWeight.Bold
            , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
        });
        root.Children.Add(new TextBlock
        {
              Text = "Enter one or more email addresses. Separate addresses with commas, semicolons, or new lines."
            , TextWrapping = TextWrapping.Wrap
            , Foreground = Brushes.Gray
        });
        root.Children.Add(this.recipients);
        root.Children.Add(this.status);

        StackPanel actions = new()
        {
              Orientation = Orientation.Horizontal
            , HorizontalAlignment = HorizontalAlignment.Right
            , Spacing = 10
        };
        Button cancel = new() { Content = "Cancel", MinWidth = 100 };
        Button send = new() { Content = "Email Report", MinWidth = 120 };
        cancel.Click += (_, _) => Close(Array.Empty<string>());
        send.Click += (_, _) => Confirm();
        actions.Children.Add(cancel);
        actions.Children.Add(send);
        root.Children.Add(actions);

        Content = root;
    }

    private void Confirm()
    {
        string[] values = (this.recipients.Text ?? string.Empty)
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length == 0)
        {
            this.status.Text = "Enter at least one email address.";
            return;
        }

        Close(values);
    }
}
