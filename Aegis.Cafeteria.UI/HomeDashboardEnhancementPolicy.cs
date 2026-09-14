namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Models;

internal static class HomeDashboardEnhancementPolicy
{
    private const string SalesChartTitle = "SALES VALUE — MORNING VS AFTERNOON — THIS WEEK";
    private const string EmployeeChartTitle = "EMPLOYEES SERVED VS ELIGIBLE — THIS WEEK";
    private const string ItemsChartTitle = "TOP 5 ITEMS SOLD — TODAY";
    private const string DashboardTitle = "CAFETERIA DAILY SALES DASHBOARD";

    public static void Apply(
        Form mainForm,
        IEmployeeParticipationQuery participationQuery,
        ICafeteriaReportService reportService)
    {
        ArgumentNullException.ThrowIfNull(mainForm);
        ArgumentNullException.ThrowIfNull(participationQuery);
        ArgumentNullException.ThrowIfNull(reportService);

        RenameDashboardTitle(mainForm);

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
        employeeChart.SetData(EmployeeChartTitle, 0, EmptyWeek());
        salesChart.SetDaypartData(SalesChartTitle, EmptySalesWeek(), "C0");

        ConfigureMetrics(mainForm);
        ConfigureCharts(new DashboardCharts(charts, salesChart, itemsChart, employeeChart));
        UpdateSubtitle(mainForm);

        System.Windows.Forms.Timer refreshTimer = new()
        {
            Interval = 60_000
        };
        bool refreshInProgress = false;

        async void RefreshDashboard(object? sender, EventArgs eventArgs)
        {
            if (refreshInProgress || mainForm.IsDisposed)
            {
                return;
            }

            refreshInProgress = true;
            try
            {
                await RefreshSalesChartAsync(reportService, salesChart);
                await RefreshEmployeeChartAsync(participationQuery, employeeChart);
                await RefreshItemsChartAsync(reportService, itemsChart);
            }
            catch
            {
                salesChart.SetDaypartData(SalesChartTitle, EmptySalesWeek(), "C0");
                employeeChart.SetData(EmployeeChartTitle, 0, EmptyWeek());
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        Control? dashboardHost = FindDashboardHost(itemsChart);
        mainForm.Shown += RefreshDashboard;
        refreshTimer.Tick += RefreshDashboard;
        if (dashboardHost is not null)
        {
            dashboardHost.VisibleChanged += RefreshDashboard;
        }

        mainForm.FormClosed += (_, _) =>
        {
            refreshTimer.Stop();
            refreshTimer.Tick -= RefreshDashboard;
            if (dashboardHost is not null)
            {
                dashboardHost.VisibleChanged -= RefreshDashboard;
            }
            refreshTimer.Dispose();
        };
        refreshTimer.Start();
    }

    private static async Task RefreshSalesChartAsync(
        ICafeteriaReportService reportService,
        WeekdayBarChartControl salesChart)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        DateOnly monday = today.AddDays(-daysSinceMonday);
        DateOnly friday = monday.AddDays(4);

        ReportDocument report = await reportService.GenerateAsync
        (
            CafeteriaReportKind.PurchaseItemDetail,
            new ReportFilter(monday, friday)
        );
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = report.Tables
            .FirstOrDefault(table => string.Equals(table.Title, "Itemized purchases", StringComparison.OrdinalIgnoreCase))?
            .Rows ?? [];

        IReadOnlyList<(string Label, decimal MorningValue, decimal AfternoonValue)> values = Enumerable
            .Range(0, 5)
            .Select(dayOffset =>
            {
                DateOnly day = monday.AddDays(dayOffset);
                decimal morning = 0m;
                decimal afternoon = 0m;

                foreach (IReadOnlyDictionary<string, object?> row in rows)
                {
                    if (!TryGetLocalDateTime(row.GetValueOrDefault("date"), out DateTime localDateTime)
                        || DateOnly.FromDateTime(localDateTime) != day)
                    {
                        continue;
                    }

                    decimal total = Convert.ToDecimal(row.GetValueOrDefault("total") ?? 0m);
                    if (localDateTime.TimeOfDay < TimeSpan.FromHours(11))
                    {
                        morning += total;
                    }
                    else
                    {
                        afternoon += total;
                    }
                }

                return (day.ToString("dddd"), morning, afternoon);
            })
            .ToArray();

        salesChart.SetDaypartData(SalesChartTitle, values, "C0");
    }

    private static bool TryGetLocalDateTime(object? value, out DateTime localDateTime)
    {
        switch (value)
        {
            case DateTimeOffset offset:
                localDateTime = offset.LocalDateTime;
                return true;
            case DateTime dateTime:
                localDateTime = dateTime;
                return true;
            default:
                localDateTime = default;
                return false;
        }
    }

    private static async Task RefreshEmployeeChartAsync(
        IEmployeeParticipationQuery participationQuery,
        EmployeeWeekBarChartControl employeeChart)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        DateOnly monday = today.AddDays(-daysSinceMonday);
        EmployeeParticipationWeekView week = await participationQuery.GetWeekAsync(monday);

        IReadOnlyList<(string Label, int Served)> values = week.Days
            .Select(day => (day.Date.ToString("dddd"), day.EmployeesServed))
            .ToArray();

        employeeChart.SetData(EmployeeChartTitle, week.EligibleEmployeeCount, values);
    }

    private static async Task RefreshItemsChartAsync(
        ICafeteriaReportService reportService,
        PieChartControl itemsChart)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        ReportDocument report = await reportService.GenerateAsync(
            CafeteriaReportKind.ProductSales,
            new ReportFilter(today, today));
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = report.Tables
            .FirstOrDefault(table => string.Equals(table.Title, "Product detail", StringComparison.OrdinalIgnoreCase))?
            .Rows ?? [];

        IReadOnlyList<(string Label, decimal Value)> sold = rows
            .Select(row =>
            (
                Label: Convert.ToString(row.GetValueOrDefault("product")) ?? "Unknown",
                Value: Convert.ToDecimal(row.GetValueOrDefault("quantity") ?? 0m)
            ))
            .Where(item => item.Value > 0m)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        (string Label, decimal Value)[] topFive = sold.Take(5).ToArray();
        decimal other = sold.Skip(5).Sum(item => item.Value);
        IReadOnlyList<(string Label, decimal Value)> chartValues = other > 0m
            ? topFive.Append(("Other", other)).ToArray()
            : topFive;

        itemsChart.SetData(ItemsChartTitle, chartValues, "N0");
    }

    private static void RenameDashboardTitle(Form mainForm)
    {
        Label? title = Descendants(mainForm)
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(
                label.Text,
                "CAFETERIA SALES DASHBOARD",
                StringComparison.OrdinalIgnoreCase));

        if (title is not null)
        {
            title.Text = DashboardTitle;
        }
    }

    private static Control? FindDashboardHost(Control itemsChart)
    {
        Control? current = itemsChart.Parent;
        while (current is not null)
        {
            if (current is Panel panel && panel.Controls.Cast<Control>().Any(control =>
                control is TableLayoutPanel layout && ContainsLabel(layout, DashboardTitle)))
            {
                return panel;
            }

            current = current.Parent;
        }

        return null;
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
        dashboard.Employees.MinimumSize = new Size(260, 330);

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
            .FirstOrDefault(label => string.Equals(
                label.Text,
                "Today's KPIs with this week's Monday–Friday sales trend.",
                StringComparison.Ordinal) || string.Equals(
                label.Text,
                "Today's KPIs with this week's Monday–Friday sales and employee activity.",
                StringComparison.Ordinal));

        if (subtitle is not null)
        {
            subtitle.Text = "Today's KPIs with this week's morning/afternoon sales and eligible-employee participation.";
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

    private static IReadOnlyList<(string Label, int Served)> EmptyWeek()
        =>
        [
            ("Monday", 0),
            ("Tuesday", 0),
            ("Wednesday", 0),
            ("Thursday", 0),
            ("Friday", 0)
        ];

    private static IReadOnlyList<(string Label, decimal MorningValue, decimal AfternoonValue)> EmptySalesWeek()
        =>
        [
            ("Monday", 0m, 0m),
            ("Tuesday", 0m, 0m),
            ("Wednesday", 0m, 0m),
            ("Thursday", 0m, 0m),
            ("Friday", 0m, 0m)
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

    private sealed record DashboardCharts(
        TableLayoutPanel Host,
        WeekdayBarChartControl Sales,
        PieChartControl Items,
        EmployeeWeekBarChartControl Employees);
}
