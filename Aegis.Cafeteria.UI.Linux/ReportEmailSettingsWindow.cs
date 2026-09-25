namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Infrastructure.Services;
using Avalonia.Controls;
using Avalonia.Layout;

internal sealed class ReportEmailSettingsWindow : Window
{
    private readonly CafeteriaManagementClient client;
    private readonly CheckBox weeklyEnabled = new() { Content = "Send weekly manager summary" };
    private readonly TextBox email = new() { MinWidth = 360 };
    private readonly ComboBox day = new() { MinWidth = 180 };
    private readonly TextBox hour = new() { Width = 90 };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button save = new() { Content = "Save Settings", MinWidth = 120 };
    private readonly Button test = new() { Content = "Send Test Email", MinWidth = 130 };

    public ReportEmailSettingsWindow(CafeteriaManagementClient client)
    {
        this.client = client;

        Title = "Cafeteria Report & Email Settings";
        Width = 640;
        Height = 430;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.day.ItemsSource = Enum.GetValues<DayOfWeek>();
        this.save.Click += async (_, _) => await SaveAsync();
        this.test.Click += async (_, _) => await SendTestAsync();
        Opened += async (_, _) => await LoadAsync();

        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        StackPanel root = new() { Margin = new Avalonia.Thickness(24), Spacing = 12 };
        root.Children.Add(new TextBlock
        {
              Text = "REPORT & EMAIL SETTINGS"
            , FontSize = 21
            , FontWeight = Avalonia.Media.FontWeight.Bold
        });
        root.Children.Add(new TextBlock
        {
              Text = "Configure the manager report destination and weekly delivery schedule. Test Email verifies the outbound email path."
            , TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        root.Children.Add(new TextBlock { Text = "Manager email" });
        root.Children.Add(this.email);
        root.Children.Add(this.weeklyEnabled);
        root.Children.Add(new TextBlock { Text = "Weekly summary day" });
        root.Children.Add(this.day);

        StackPanel hourRow = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        hourRow.Children.Add(new TextBlock { Text = "Hour (0-23)", VerticalAlignment = VerticalAlignment.Center });
        hourRow.Children.Add(this.hour);
        root.Children.Add(hourRow);

        root.Children.Add(this.status);

        StackPanel actions = new()
        {
              Orientation = Orientation.Horizontal
            , HorizontalAlignment = HorizontalAlignment.Right
            , Spacing = 10
        };
        Button close = new() { Content = "Close", MinWidth = 90 };
        close.Click += (_, _) => Close();
        actions.Children.Add(this.save);
        actions.Children.Add(this.test);
        actions.Children.Add(close);
        root.Children.Add(actions);
        return root;
    }

    private async Task LoadAsync()
    {
        ToggleBusy(true);
        try
        {
            ManagerReportSettingsSnapshot settings = await this.client.GetReportSettingsAsync();
            this.weeklyEnabled.IsChecked = settings.WeeklySummaryEnabled;
            this.email.Text = settings.EmailAddress ?? string.Empty;
            this.day.SelectedItem = settings.WeeklySummaryDay;
            this.hour.Text = settings.WeeklySummaryHourLocal.ToString();
            this.status.Text = "Settings loaded.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not load settings: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task SaveAsync()
    {
        if (!int.TryParse(this.hour.Text, out int selectedHour) || selectedHour is < 0 or > 23)
        {
            this.status.Text = "Enter an hour from 0 through 23.";
            return;
        }

        ToggleBusy(true);
        try
        {
            DayOfWeek selectedDay = this.day.SelectedItem is DayOfWeek value ? value : DayOfWeek.Monday;
            ManagerReportSettingsSnapshot settings = await this.client.SetReportSettingsAsync(
                  this.weeklyEnabled.IsChecked == true
                , string.IsNullOrWhiteSpace(this.email.Text) ? null : this.email.Text.Trim()
                , selectedDay
                , selectedHour);
            this.email.Text = settings.EmailAddress ?? string.Empty;
            this.status.Text = "Report and email settings saved.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not save settings: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task SendTestAsync()
    {
        string address = this.email.Text?.Trim() ?? string.Empty;
        if (address.Length == 0)
        {
            this.status.Text = "Enter a manager email address first.";
            return;
        }

        ToggleBusy(true);
        try
        {
            await this.client.SendTestEmailAsync(address);
            this.status.Text = $"Test email queued for {address}.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Test email failed: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private void ToggleBusy(bool busy)
    {
        this.save.IsEnabled = !busy;
        this.test.IsEnabled = !busy;
    }
}
