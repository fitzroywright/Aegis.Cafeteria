using Aegis.Cafeteria.Application.Integration;

namespace Aegis.Cafeteria.UI;

internal sealed class CacheStatusDialog : Form
{
    private readonly ICustomerReferenceCacheStatus statusProvider;
    private readonly ReferenceDataRefreshService? refresh;
    private readonly Label summary = new() { AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
    private readonly TextBox details = new() { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Dock = DockStyle.Fill, Font = new Font("Consolas", 10) };
    private readonly Button refreshButton = new() { Text = "Refresh all", Width = 120, Height = 36 };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Height = 18, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Label progressText = new() { AutoSize = true, MaximumSize = new Size(540, 0) };
    private readonly System.Windows.Forms.Timer progressTimer = new() { Interval = 500 };
    private bool readingStatus;

    public CacheStatusDialog(ICustomerReferenceCacheStatus statusProvider, ReferenceDataRefreshService? refresh)
    {
        this.statusProvider = statusProvider;
        this.refresh = refresh;
        Text = "Reference Data Status";
        StartPosition = FormStartPosition.CenterParent;
        Width = 620;
        Height = 460;
        MinimizeBox = false;
        MaximizeBox = false;
        Font = new Font("Segoe UI", 10);
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1, Padding = new Padding(20) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (int index = 0; index < 4; index++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(summary, 0, 0);
        root.Controls.Add(details, 0, 1);
        root.Controls.Add(new Label
        {
              AutoSize = true
            , Text = "Employee and holiday refresh runs in the background. You may close this window and continue serving customers."
            , MaximumSize = new Size(540, 0)
            , Margin = new Padding(0, 10, 0, 10)
        }, 0, 2);
        root.Controls.Add(progress, 0, 3);
        root.Controls.Add(progressText, 0, 4);
        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        Button close = new() { Text = "Close", Width = 90, Height = 36 };
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        if (refresh is not null)
        {
            buttons.Controls.Add(refreshButton);
            refreshButton.Click += (_, _) =>
            {
                refresh.StartBackgroundRefresh(DateOnly.FromDateTime(DateTime.Today));
                refreshButton.Enabled = false;
                progress.Visible = true;
                progressText.Text = "Starting background refresh...";
            };
        }
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);
        AcceptButton = close;
        Shown += async (_, _) => { progressTimer.Start(); await ReloadAsync(); };
        progressTimer.Tick += async (_, _) => await ReloadAsync();
        FormClosed += (_, _) => progressTimer.Dispose();
    }

    private async Task ReloadAsync()
    {
        if (readingStatus || IsDisposed) return;
        readingStatus = true;
        try
        {
            CustomerReferenceCacheStatus state = await Task.Run(() => statusProvider.GetAsync());
            if (IsDisposed || Disposing) return;
            ReferenceRefreshRunStatus? run = refresh?.BackgroundStatus;
            bool running = state.RefreshInProgress || run?.Running == true;
            refreshButton.Enabled = !running;
            progress.Visible = running;
            progressText.Text = state.RefreshInProgress
                ? $"{state.RefreshedEmployeeCount:N0} employees saved • {state.CompletedBatchCount:N0} batches completed"
                : state.LastError is not null ? $"Employee refresh failed: {state.LastError}" : run?.Message ?? "Ready to refresh";
            summary.Text = $"{state.EmployeeCount:N0} employees cached";
            details.Text = string.Join(Environment.NewLine,
                $"Employees:            {state.EmployeeCount:N0}",
                $"Identifiers:          {state.IdentifierCount:N0}",
                $"Last refresh attempt: {Local(state.LastAttemptUtc)}",
                $"Last refresh success: {Local(state.LastSuccessUtc)}",
                $"Employees saved:      {state.RefreshedEmployeeCount:N0}",
                $"Completed batches:    {state.CompletedBatchCount:N0}",
                $"Last error:           {state.LastError ?? "None"}");
        }
        catch (Exception exception)
        {
            if (!IsDisposed) progressText.Text = $"Status unavailable: {exception.GetBaseException().Message}";
        }
        finally { readingStatus = false; }
    }

    private static string Local(DateTimeOffset? value) => value?.ToLocalTime().ToString("MMM dd yyyy hh:mm tt") ?? "Never";
}
