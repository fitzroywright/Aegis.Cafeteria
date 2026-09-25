namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Infrastructure.Services;

public sealed class AutomaticOperationsDialog : Form
{
    private readonly CafeteriaServicesClient services;
    private readonly Label status = new()
    {
          AutoSize = true
        , MaximumSize = new Size(650, 0)
        , ForeColor = Color.DimGray
    };

    private readonly DateTimePicker month = new()
    {
          Format = DateTimePickerFormat.Custom
        , CustomFormat = "MMMM yyyy"
        , ShowUpDown = true
        , Width = 180
    };

    public AutomaticOperationsDialog(CafeteriaServicesClient services)
    {
        this.services = services;

        Text = "Cafeteria Automatic Operations";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(720, 500);
        Font = new Font("Segoe UI", 10);

        DateTime previous = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        this.month.Value = previous;

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
              Dock = DockStyle.Fill
            , Padding = new Padding(24)
            , ColumnCount = 1
            , RowCount = 8
        };

        root.Controls.Add(new Label
        {
              AutoSize = true
            , Text = "Automatic Operations"
            , Font = new Font("Segoe UI", 18, FontStyle.Bold)
            , ForeColor = Color.FromArgb(24, 45, 68)
        });

        root.Controls.Add(new Label
        {
              AutoSize = true
            , MaximumSize = new Size(650, 0)
            , Text = "These are exception controls for service-owned automatic jobs. Use them only when an automatic operation did not complete or must be re-run."
            , ForeColor = Color.DimGray
            , Margin = new Padding(0, 4, 0, 18)
        });

        FlowLayoutPanel period = new()
        {
              AutoSize = true
            , FlowDirection = FlowDirection.LeftToRight
            , WrapContents = false
        };
        period.Controls.Add(new Label { Text = "Selected month", AutoSize = true, Margin = new Padding(0, 8, 10, 0) });
        period.Controls.Add(this.month);
        root.Controls.Add(period);

        root.Controls.Add(Section(
            "Month lifecycle",
            ("Retry Month Opening", RetryOpenAsync),
            ("Recalculate Selected Month", RecalculateAsync),
            ("Retry Month Close", RetryCloseAsync)));

        root.Controls.Add(Section(
            "Scheduled reports",
            ("Resend Yesterday Daily Sales", ResendDailySalesAsync),
            ("Resend Previous Week Manager Summary", ResendWeeklyManagerAsync)));

        root.Controls.Add(new Label
        {
              AutoSize = true
            , Text = "Normal opening, closing and scheduled reporting remain automatic."
            , ForeColor = Color.DimGray
            , Margin = new Padding(0, 12, 0, 4)
        });

        root.Controls.Add(this.status);

        Button close = new()
        {
              Text = "Close"
            , Width = 100
            , Height = 36
            , DialogResult = DialogResult.Cancel
            , Anchor = AnchorStyles.Right
        };
        root.Controls.Add(close);
        CancelButton = close;

        return root;
    }

    private Control Section(string title, params (string Caption, Func<Task> Action)[] actions)
    {
        GroupBox group = new()
        {
              Text = title
            , AutoSize = true
            , AutoSizeMode = AutoSizeMode.GrowAndShrink
            , Dock = DockStyle.Top
            , Padding = new Padding(14)
            , Margin = new Padding(0, 14, 0, 0)
        };

        FlowLayoutPanel buttons = new()
        {
              AutoSize = true
            , FlowDirection = FlowDirection.LeftToRight
            , WrapContents = true
            , Dock = DockStyle.Fill
        };

        foreach ((string caption, Func<Task> action) in actions)
        {
            Button button = new()
            {
                  Text = caption
                , AutoSize = true
                , Height = 38
                , Margin = new Padding(0, 0, 10, 0)
            };

            button.Click += async (_, _) =>
            {
                button.Enabled = false;
                try
                {
                    this.status.ForeColor = Color.DimGray;
                    this.status.Text = $"Running {caption}...";
                    await action();
                }
                catch (Exception exception)
                {
                    this.status.ForeColor = Color.Firebrick;
                    this.status.Text = exception.GetBaseException().Message;
                }
                finally
                {
                    button.Enabled = true;
                }
            };

            buttons.Controls.Add(button);
        }

        group.Controls.Add(buttons);
        return group;
    }

    private async Task RetryOpenAsync()
    {
        int year = this.month.Value.Year;
        int selectedMonth = this.month.Value.Month;
        MonthOperationResult result = await this.services.RetryMonthOpeningAsync(year, selectedMonth);
        Success($"Month {year:D4}-{selectedMonth:D2} opening checked. New allocations created: {result.Created.GetValueOrDefault()}.");
    }

    private async Task RecalculateAsync()
    {
        int year = this.month.Value.Year;
        int selectedMonth = this.month.Value.Month;

        DialogResult confirmation = MessageBox.Show(
            this,
            $"Recalculate {year:D4}-{selectedMonth:D2}? This is an exception operation and Services will refuse it after financial activity.",
            "Recalculate Month",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        MonthOperationResult result = await this.services.RecalculateMonthAsync(year, selectedMonth);
        Success($"Month recalculation completed. Updated: {result.Updated.GetValueOrDefault()}, unchanged: {result.Unchanged.GetValueOrDefault()}.");
    }

    private async Task RetryCloseAsync()
    {
        int year = this.month.Value.Year;
        int selectedMonth = this.month.Value.Month;
        MonthOperationResult result = await this.services.RetryMonthCloseAsync(year, selectedMonth);
        Success(result.IsClosed == true
            ? $"Month {year:D4}-{selectedMonth:D2} is closed. Newly closed employees: {result.NewlyClosedEmployees.GetValueOrDefault()}."
            : $"Month {year:D4}-{selectedMonth:D2} is not yet fully closed.");
    }

    private async Task ResendDailySalesAsync()
    {
        DateOnly date = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        await this.services.ResendDailySalesReportAsync(date);
        Success($"Daily Sales Report for {date:yyyy-MM-dd} was queued for resend.");
    }

    private async Task ResendWeeklyManagerAsync()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        int daysSinceMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        DateOnly start = today.AddDays(-daysSinceMonday).AddDays(-7);
        await this.services.ResendManagerWeeklySummaryAsync(start);
        Success($"Manager weekly summary for {start:yyyy-MM-dd} through {start.AddDays(6):yyyy-MM-dd} was queued for resend.");
    }

    private void Success(string message)
    {
        this.status.ForeColor = Color.DarkGreen;
        this.status.Text = message;
    }
}
