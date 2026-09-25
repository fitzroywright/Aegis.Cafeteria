using Aegis.Cafeteria.Infrastructure.Services;

namespace Aegis.Cafeteria.UI;

internal sealed class EmployeeAmountDueDialog : Form
{
    private readonly CafeteriaManagementClient client;
    private readonly NumericUpDown year = new() { Minimum = 2000, Maximum = 9999, Width = 90 };
    private readonly NumericUpDown month = new() { Minimum = 1, Maximum = 12, Width = 70 };
    private readonly Label summary = new() { AutoSize = true, ForeColor = Color.FromArgb(24, 45, 68), Font = new Font("Segoe UI", 11, FontStyle.Bold) };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly DataGridView grid = new()
    {
          Dock = DockStyle.Fill
        , ReadOnly = true
        , AllowUserToAddRows = false
        , AllowUserToDeleteRows = false
        , AutoGenerateColumns = true
        , AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };
    private readonly Button load = new() { Text = "Load Report", Width = 110, Height = 38 };
    private readonly Button send = new() { Text = "Send Email Report", Width = 145, Height = 38, Enabled = false };

    public EmployeeAmountDueDialog(CafeteriaManagementClient client)
    {
        this.client = client;

        DateTime previous = DateTime.Today.AddMonths(-1);
        this.year.Value = previous.Year;
        this.month.Value = previous.Month;

        Text = "Employee Amount Due";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 620);
        Size = new Size(1120, 700);
        Font = new Font("Segoe UI", 10);

        this.load.Click += async (_, _) => await LoadReportAsync();
        this.send.Click += async (_, _) => await SendReportAsync();

        Controls.Add(BuildLayout());
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
              Dock = DockStyle.Fill
            , Padding = new Padding(22)
            , RowCount = 5
            , ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label heading = new()
        {
              Text = "EMPLOYEE AMOUNT DUE"
            , AutoSize = true
            , Font = new Font("Segoe UI", 16, FontStyle.Bold)
            , ForeColor = Color.FromArgb(24, 45, 68)
        };
        root.Controls.Add(heading);

        FlowLayoutPanel period = new() { AutoSize = true, Margin = new Padding(0, 14, 0, 10) };
        period.Controls.Add(new Label { Text = "Year", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        period.Controls.Add(this.year);
        period.Controls.Add(new Label { Text = "Month", AutoSize = true, Margin = new Padding(18, 8, 6, 0) });
        period.Controls.Add(this.month);
        period.Controls.Add(this.load);
        period.Controls.Add(this.send);
        root.Controls.Add(period);

        root.Controls.Add(this.summary);
        root.Controls.Add(this.grid);

        FlowLayoutPanel footer = new()
        {
              AutoSize = true
            , Dock = DockStyle.Fill
            , FlowDirection = FlowDirection.RightToLeft
        };
        Button close = new() { Text = "Close", Width = 95, Height = 38, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(close);
        footer.Controls.Add(this.status);
        root.Controls.Add(footer);

        return root;
    }

    private async Task LoadReportAsync()
    {
        ToggleBusy(true);
        try
        {
            PayrollDeductionReportSnapshot report = await this.client.GetPayrollReportAsync(
                decimal.ToInt32(this.year.Value),
                decimal.ToInt32(this.month.Value));

            this.grid.DataSource = report.Details;
            this.summary.Text =
                $"{report.Year:D4}-{report.Month:D2}  |  Employees with amount due: {report.EmployeesWithAmountDue:N0}  |  Total: {report.TotalEmployeeAmountDue:C}";
            this.send.Enabled = true;
            this.status.Text = "Report loaded.";
        }
        catch (Exception exception)
        {
            this.grid.DataSource = null;
            this.summary.Text = string.Empty;
            this.send.Enabled = false;
            this.status.Text = $"Could not load report: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task SendReportAsync()
    {
        ToggleBusy(true);
        try
        {
            int selectedYear = decimal.ToInt32(this.year.Value);
            int selectedMonth = decimal.ToInt32(this.month.Value);
            await this.client.SendPayrollReportAsync(selectedYear, selectedMonth, force: true);
            this.status.Text = $"Employee Amount Due email report for {selectedYear:D4}-{selectedMonth:D2} was queued.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not queue report: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private void ToggleBusy(bool busy)
    {
        this.load.Enabled = !busy;
        if (busy) this.send.Enabled = false;
        UseWaitCursor = busy;
    }
}
