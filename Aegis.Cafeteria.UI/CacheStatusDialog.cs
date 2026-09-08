using Aegis.Cafeteria.Application.Integration;

namespace Aegis.Cafeteria.UI;

internal sealed class CacheStatusDialog : Form
{
    private readonly ICustomerReferenceCacheStatus statusProvider;
    private readonly Func<Task<int>>? refresh;
    private readonly Label summary = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly Button refreshButton = new() { Text = "Refresh all", Width = 120, Height = 36 };

    public CacheStatusDialog(ICustomerReferenceCacheStatus statusProvider, Func<Task<int>>? refresh)
    {
        this.statusProvider = statusProvider;
        this.refresh = refresh;
        Text = "Reference Data Status";
        StartPosition = FormStartPosition.CenterParent;
        Width = 620;
        Height = 430;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(this.summary, 0, 0);
        root.Controls.Add(this.details, 0, 1);

        var note = new Label
        {
            AutoSize = true,
            Text = "Customers and cafeteria funding reference data are cached locally. Refresh all updates employees, holidays and the company subsidy policy.",
            ForeColor = Color.DimGray,
            MaximumSize = new Size(540, 0),
            Margin = new Padding(0, 10, 0, 10)
        };
        root.Controls.Add(note, 0, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var close = new Button { Text = "Close", Width = 90, Height = 36, DialogResult = DialogResult.OK };
        buttons.Controls.Add(close);
        if (this.refresh is not null)
        {
            buttons.Controls.Add(this.refreshButton);
            this.refreshButton.Click += async (_, _) => await RefreshNowAsync();
        }
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
        AcceptButton = close;
        Shown += async (_, _) => await ReloadAsync();
    }

    private async Task RefreshNowAsync()
    {
        if (this.refresh is null) return;
        try
        {
            this.refreshButton.Enabled = false;
            this.summary.Text = "Refreshing employees, holidays and funding policy...";
            await this.refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.GetBaseException().Message, "Employee cache refresh failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            this.refreshButton.Enabled = true;
            await ReloadAsync();
        }
    }

    private async Task ReloadAsync()
    {
        var status = await this.statusProvider.GetAsync();
        var now = DateTimeOffset.UtcNow;
        var age = status.Age(now);
        this.summary.Text = status.EmployeeCount == 0
            ? "No employees are currently cached"
            : $"{status.EmployeeCount:N0} employees cached";
        this.details.Text = string.Join(Environment.NewLine,
            $"Employees:            {status.EmployeeCount:N0}",
            $"Identifiers:          {status.IdentifierCount:N0}",
            $"Cache updated:        {Local(status.CacheUpdatedAtUtc)}",
            $"Cache age:            {FormatAge(age)}",
            $"Last refresh attempt: {Local(status.LastAttemptUtc)}",
            $"Last refresh success: {Local(status.LastSuccessUtc)}",
            $"Refresh in progress:  {(status.RefreshInProgress ? "Yes" : "No")}",
            $"Last error:            {status.LastError ?? "None"}");
    }

    private static string Local(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
    private static string FormatAge(TimeSpan? age)
    {
        if (!age.HasValue) return "Unknown";
        if (age.Value.TotalMinutes < 1) return "Less than 1 minute";
        if (age.Value.TotalHours < 1) return $"{(int)age.Value.TotalMinutes} minutes";
        if (age.Value.TotalDays < 1) return $"{age.Value.TotalHours:0.0} hours";
        return $"{age.Value.TotalDays:0.0} days";
    }
}
