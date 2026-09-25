using Aegis.Cafeteria.Infrastructure.Services;

namespace Aegis.Cafeteria.UI;

internal sealed class ReportEmailSettingsDialog : Form
{
    private readonly CafeteriaManagementClient client;
    private readonly CheckBox weeklyEnabled = new() { Text = "Send weekly manager summary", AutoSize = true };
    private readonly TextBox email = new() { Width = 360 };
    private readonly ComboBox day = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly NumericUpDown hour = new() { Minimum = 0, Maximum = 23, Width = 90 };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(520, 0) };
    private readonly Button save = new() { Text = "Save Settings", Width = 125, Height = 38 };
    private readonly Button test = new() { Text = "Send Test Email", Width = 135, Height = 38 };

    public ReportEmailSettingsDialog(CafeteriaManagementClient client)
    {
        this.client = client;

        Text = "Cafeteria Report & Email Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 390);
        Font = new Font("Segoe UI", 10);

        this.day.DataSource = Enum.GetValues<DayOfWeek>();
        this.save.Click += async (_, _) => await SaveAsync();
        this.test.Click += async (_, _) => await SendTestAsync();
        Shown += async (_, _) => await LoadAsync();

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
              Dock = DockStyle.Fill
            , Padding = new Padding(24)
            , ColumnCount = 2
            , RowCount = 7
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Label heading = new()
        {
              Text = "REPORT & EMAIL SETTINGS"
            , AutoSize = true
            , Font = new Font("Segoe UI", 16, FontStyle.Bold)
            , ForeColor = Color.FromArgb(24, 45, 68)
        };
        root.Controls.Add(heading, 0, 0);
        root.SetColumnSpan(heading, 2);

        Label note = new()
        {
              Text = "Configure the cafeteria manager report destination and weekly delivery schedule. Test Email verifies the configured outbound email path."
            , AutoSize = true
            , MaximumSize = new Size(540, 0)
            , ForeColor = Color.DimGray
            , Margin = new Padding(0, 6, 0, 18)
        };
        root.Controls.Add(note, 0, 1);
        root.SetColumnSpan(note, 2);

        root.Controls.Add(new Label { Text = "Manager email", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        root.Controls.Add(this.email, 1, 2);
        root.Controls.Add(new Label { Text = "Weekly summary", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        root.Controls.Add(this.weeklyEnabled, 1, 3);
        root.Controls.Add(new Label { Text = "Summary day", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        root.Controls.Add(this.day, 1, 4);
        root.Controls.Add(new Label { Text = "Hour (0-23)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        root.Controls.Add(this.hour, 1, 5);

        FlowLayoutPanel actions = new()
        {
              AutoSize = true
            , FlowDirection = FlowDirection.RightToLeft
            , Dock = DockStyle.Fill
            , Margin = new Padding(0, 18, 0, 0)
        };
        Button close = new() { Text = "Close", Width = 95, Height = 38, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(close);
        actions.Controls.Add(this.test);
        actions.Controls.Add(this.save);
        root.Controls.Add(actions, 0, 6);
        root.SetColumnSpan(actions, 2);

        this.status.Dock = DockStyle.Bottom;
        this.status.Padding = new Padding(24, 0, 24, 16);
        Panel host = new() { Dock = DockStyle.Fill };
        host.Controls.Add(root);
        host.Controls.Add(this.status);
        return host;
    }

    private async Task LoadAsync()
    {
        ToggleBusy(true);
        try
        {
            ManagerReportSettingsSnapshot settings = await this.client.GetReportSettingsAsync();
            this.weeklyEnabled.Checked = settings.WeeklySummaryEnabled;
            this.email.Text = settings.EmailAddress ?? string.Empty;
            this.day.SelectedItem = settings.WeeklySummaryDay;
            this.hour.Value = Math.Clamp(settings.WeeklySummaryHourLocal, 0, 23);
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
        ToggleBusy(true);
        try
        {
            DayOfWeek selectedDay = this.day.SelectedItem is DayOfWeek value ? value : DayOfWeek.Monday;
            ManagerReportSettingsSnapshot settings = await this.client.SetReportSettingsAsync(
                  this.weeklyEnabled.Checked
                , string.IsNullOrWhiteSpace(this.email.Text) ? null : this.email.Text.Trim()
                , selectedDay
                , decimal.ToInt32(this.hour.Value));

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
        string address = this.email.Text.Trim();
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
        this.save.Enabled = !busy;
        this.test.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
