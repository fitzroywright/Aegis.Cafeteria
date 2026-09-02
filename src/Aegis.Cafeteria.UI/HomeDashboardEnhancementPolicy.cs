namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Models;

internal static class HomeDashboardEnhancementPolicy
{
    private const string EmployeeChartTitle = "EMPLOYEES SERVED BY DAY — THIS WEEK";
    private const string CashCustomerName = "Cash Customer";

    public static void Apply(Form mainForm, ICafeteriaReportService reportService)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(reportService);

        WeekdayBarChartControl? salesChart = Descendants(mainForm)
            .OfType<WeekdayBarChartControl>()
            .FirstOrDefault();
        PieChartControl? itemsChart = Descendants(mainForm)
            .OfType<PieChartControl>()
            .FirstOrDefault();

        if (salesChart?.Parent is not TableLayoutPanel charts || itemsChart?.Parent != charts)
        {
            return;
        }

        EmployeeWeekBarChartControl employeeChart = new()
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 0, 0)
        };
        employeeChart.SetData(EmployeeChartTitle, EmptyWeek());

        ConfigureMetrics(mainForm);
        ConfigureCharts(new DashboardCharts(charts, salesChart, itemsChart, employeeChart));
        UpdateSubtitle(mainForm);

        System.Windows.Forms.Timer refreshTimer = new()
        {
            Interval = 60_000
        };
        bool refreshInProgress = false;

        async void RefreshEmployeeChart(object? sender, EventArgs eventArgs)
        {
            if (refreshInProgress || mainForm.IsDisposed)
            {
                return;
            }

            refreshInProgress = true;
            try
            {
                await RefreshEmployeeChartAsync(reportService, employeeChart);
            }
            catch
            {
                employeeChart.SetData(EmployeeChartTitle, EmptyWeek());
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        mainForm.Shown += RefreshEmployeeChart;
        refreshTimer.Tick += RefreshEmployeeChart;
        mainForm.FormClosed += (_, _) =>
        {
            refreshTimer.Stop();
            refreshTimer.Tick -= RefreshEmployeeChart;
            refreshTimer.Dispose();
        };
        refreshTimer.Start();
    }

    private static async Task RefreshEmployeeChartAsync
    (
        ICafeteriaReportService reportService,
        EmployeeWeekBarChartControl employeeChart
    )
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        DateOnly monday = today.AddDays(-daysSinceMonday);
        DateOnly friday = monday.AddDays(4);

        CafeteriaReport report = await reportService.GenerateAsync
        (
            CafeteriaReportKind.PurchaseByPerson,
            new ReportFilter(monday, friday, Grouping: PurchaseGrouping.Day)
        );

        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = report.Tables
            .FirstOrDefault(table => table.Title.StartsWith("Totals by person", StringComparison.OrdinalIgnoreCase))?
            .Rows ?? [];

        List<(string Label, int Value)> values = [];
        for (int dayOffset = 0; dayOffset < 5; dayOffset++)
        {
            DateOnly day = monday.AddDays(dayOffset);
            string periodLabel = day.ToString("dd MMM yyyy");
            int employeeCount = rows.Count(row =>
                !string.Equals
                (
                    Convert.ToString(row.GetValueOrDefault("customer")),
                    CashCustomerName,
                    StringComparison.OrdinalIgnoreCase
                )
                && string.Equals
                (
                    Convert.ToString(row.GetValueOrDefault("period")),
                    periodLabel,
                    StringComparison.OrdinalIgnoreCase
                ));
            values.Add((day.ToString("dddd"), employeeCount));
        }

        employeeChart.SetData(EmployeeChartTitle, values);
    }

    private static void ConfigureMetrics(Form mainForm)
    {
        string[] captions = ["NET SALES", "TRANSACTIONS", "REFUNDS"];
        TableLayoutPanel? metrics = Descendants(mainForm)
            .OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => IsMetricsPanel(panel, captions));

        if (metrics is null)
        {
            return;
        }

        Control[] cards = captions
            .Select(caption => metrics.Controls.Cast<Control>().First(control => ContainsLabel(control, caption)))
            .ToArray();

        metrics.SuspendLayout();
        metrics.Controls.Clear();
        metrics.ColumnStyles.Clear();
        metrics.RowStyles.Clear();
        metrics.ColumnCount = 3;
        metrics.RowCount = 1;
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        for (int index = 0; index < cards.Length; index++)
        {
            Control card = cards[index];
            card.Dock = DockStyle.Fill;
            card.Margin = index == cards.Length - 1
                ? Padding.Empty
                : new Padding(0, 0, 8, 0);
            metrics.Controls.Add(card, index, 0);
        }
        metrics.ResumeLayout(true);
    }

    private static void ConfigureCharts(DashboardCharts dashboard)
    {
        TableLayoutPanel charts = dashboard.Host;
        charts.SuspendLayout();
        charts.Controls.Clear();
        charts.ColumnStyles.Clear();
        charts.RowStyles.Clear();
        charts.ColumnCount = 2;
        charts.RowCount = 2;
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        charts.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        charts.MinimumSize = new Size(0, 350);

        dashboard.Sales.Dock = DockStyle.Fill;
        dashboard.Sales.MinimumSize = new Size(0, 160);
        dashboard.Sales.Margin = new Padding(0, 0, 8, 5);
        dashboard.Items.Dock = DockStyle.Fill;
        dashboard.Items.MinimumSize = new Size(0, 160);
        dashboard.Items.Margin = new Padding(0, 5, 8, 0);
        dashboard.Employees.MinimumSize = new Size(210, 330);

        charts.Controls.Add(dashboard.Sales, 0, 0);
        charts.Controls.Add(dashboard.Items, 0, 1);
        charts.Controls.Add(dashboard.Employees, 1, 0);
        charts.SetRowSpan(dashboard.Employees, 2);
        charts.ResumeLayout(true);
    }

    private static void UpdateSubtitle(Form mainForm)
    {
        Label? subtitle = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals
            (
                label.Text,
                "Today's KPIs with this week's Monday–Friday sales trend.",
                StringComparison.Ordinal
            ));

        if (subtitle is not null)
        {
            subtitle.Text = "Today's KPIs with this week's Monday–Friday sales and employee activity.";
        }
    }

    private static bool IsMetricsPanel(TableLayoutPanel panel, IReadOnlyCollection<string> captions)
    {
        int matchingCards = panel.Controls.Cast<Control>().Count(control =>
            captions.Count(caption => ContainsLabel(control, caption)) == 1);
        return matchingCards == captions.Count;
    }

    private static bool ContainsLabel(Control root, string text)
    {
        if (root is Label rootLabel && string.Equals(rootLabel.Text, text, StringComparison.Ordinal))
        {
            return true;
        }

        return Descendants(root)
            .OfType<Label>()
            .Any(label => string.Equals(label.Text, text, StringComparison.Ordinal));
    }

    private static IReadOnlyList<(string Label, int Value)> EmptyWeek()
        =>
        [
            ("Monday", 0),
            ("Tuesday", 0),
            ("Wednesday", 0),
            ("Thursday", 0),
            ("Friday", 0)
        ];

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record DashboardCharts
    (
        TableLayoutPanel Host,
        WeekdayBarChartControl Sales,
        PieChartControl Items,
        EmployeeWeekBarChartControl Employees
    );
}
