namespace Aegis.Cafeteria.UI.Linux;

using Avalonia.Controls;
using Avalonia.Layout;
using System.Net.Mail;

public sealed record WeeklySummaryPreferenceResult(bool Enabled, string? EmailAddress);

public sealed class WeeklySummaryPreferenceWindow : Window
{
    private readonly TextBox emailAddress = new() { Width = 330 };

    public WeeklySummaryPreferenceWindow(string customerName, string? suggestedEmailAddress = null, bool needsReview = false)
    {
        Title = needsReview ? "Confirm email address" : "Weekly Cafeteria Summary";
        Width = 600;
        Height = 310;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.emailAddress.Text = suggestedEmailAddress ?? string.Empty;

        TextBlock prompt = new()
        {
            Text = needsReview
                ? $"{customerName}'s work email details have changed. Keep the address below, edit it, stop emails, or ask later."
                : $"Ask {customerName} if they would like a weekly email summary of their cafeteria transactions.",
            Foreground = needsReview ? Avalonia.Media.Brushes.DarkGoldenrod : Avalonia.Media.Brushes.Black,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };
        TextBlock emailLabel = new() { Text = "Email address" };
        Button yes = new() { Content = needsReview ? "Keep / save address" : "Enable", Width = 160 };
        Button no = new() { Content = needsReview ? "Stop emails" : "No thanks", Width = 100 };
        Button later = new() { Content = "Ask later", Width = 100 };
        later.Click += (_, _) => Close();
        yes.Click += (_, _) =>
        {
            try
            {
                MailAddress address = new(this.emailAddress.Text?.Trim() ?? string.Empty);
                Close(new WeeklySummaryPreferenceResult(true, address.Address));
            }
            catch (FormatException)
            {
                emailLabel.Text = "Enter a valid email address";
            }
        };
        no.Click += (_, _) => Close(new WeeklySummaryPreferenceResult(false, null));

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(yes);
        buttons.Children.Add(no);
        buttons.Children.Add(later);
        StackPanel layout = new() { Margin = new Avalonia.Thickness(24), Spacing = 12 };
        layout.Children.Add(prompt);
        layout.Children.Add(emailLabel);
        layout.Children.Add(this.emailAddress);
        layout.Children.Add(buttons);
        Content = layout;
    }
}

