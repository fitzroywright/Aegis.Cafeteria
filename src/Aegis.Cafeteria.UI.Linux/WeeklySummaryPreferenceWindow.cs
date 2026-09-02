namespace Aegis.Cafeteria.UI.Linux;

using Avalonia.Controls;
using Avalonia.Layout;
using System.Net.Mail;

public sealed class WeeklySummaryPreferenceWindow : Window
{
    private readonly TextBox emailAddress = new() { Width = 330 };

    public WeeklySummaryPreferenceWindow(string customerName, string? suggestedEmailAddress = null)
    {
        Title = "Weekly Cafeteria Summary";
        Width = 500;
        Height = 250;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.emailAddress.Text = suggestedEmailAddress ?? string.Empty;

        TextBlock prompt = new()
        {
            Text = $"Ask {customerName} if they would like a weekly email summary of their cafeteria transactions.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        TextBlock emailLabel = new() { Text = "Email address" };
        Button yes = new() { Content = "Enable", Width = 100 };
        Button no = new() { Content = "No thanks", Width = 100 };
        yes.Click += (_, _) =>
        {
            try
            {
                MailAddress address = new(this.emailAddress.Text?.Trim() ?? string.Empty);
                this.emailAddress.Text = address.Address;
                Close(true);
            }
            catch (FormatException)
            {
                emailLabel.Text = "Enter a valid email address";
            }
        };
        no.Click += (_, _) => Close(false);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        StackPanel layout = new() { Margin = new Avalonia.Thickness(24), Spacing = 12 };
        layout.Children.Add(prompt);
        layout.Children.Add(emailLabel);
        layout.Children.Add(this.emailAddress);
        layout.Children.Add(buttons);
        Content = layout;
    }

    public string EmailAddress => this.emailAddress.Text?.Trim() ?? string.Empty;
}
