namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Infrastructure.Services;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Collections.ObjectModel;

internal sealed class EmployeeAmountDueWindow : Window
{
    private readonly CafeteriaManagementClient client;
    private readonly TextBox year = new() { Width = 90 };
    private readonly TextBox month = new() { Width = 70 };
    private readonly TextBlock summary = new() { FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = true };
    private readonly Button load = new() { Content = "Load Report", MinWidth = 105 };
    private readonly Button send = new() { Content = "Send Email Report", MinWidth = 140, IsEnabled = false };

    public EmployeeAmountDueWindow(CafeteriaManagementClient client)
    {
        this.client = client;

        DateTime previous = DateTime.Today.AddMonths(-1);
        this.year.Text = previous.Year.ToString();
        this.month.Text = previous.Month.ToString();

        Title = "Employee Amount Due";
        Width = 1120;
        Height = 700;
        MinWidth = 900;
        MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.load.Click += async (_, _) => await LoadReportAsync();
        this.send.Click += async (_, _) => await SendReportAsync();

        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        Grid root = new() { Margin = new Avalonia.Thickness(22), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock heading = new()
        {
              Text = "EMPLOYEE AMOUNT DUE"
            , FontSize = 21
            , FontWeight = Avalonia.Media.FontWeight.Bold
        };
        root.Children.Add(heading);

        StackPanel period = new() { Orientation = Orientation.Horizontal, Spacing = 9 };
        period.Children.Add(new TextBlock { Text = "Year", VerticalAlignment = VerticalAlignment.Center });
        period.Children.Add(this.year);
        period.Children.Add(new TextBlock { Text = "Month", VerticalAlignment = VerticalAlignment.Center });
        period.Children.Add(this.month);
        period.Children.Add(this.load);
        period.Children.Add(this.send);
        Grid.SetRow(period, 1);
        root.Children.Add(period);

        Grid.SetRow(this.summary, 2);
        root.Children.Add(this.summary);

        Grid.SetRow(this.grid, 3);
        root.Children.Add(this.grid);

        StackPanel footer = new()
        {
              Orientation = Orientation.Horizontal
            , HorizontalAlignment = HorizontalAlignment.Right
            , Spacing = 10
        };
        footer.Children.Add(this.status);
        Button close = new() { Content = "Close", MinWidth = 90 };
        close.Click += (_, _) => Close();
        footer.Children.Add(close);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return root;
    }

    private bool TryPeriod(out int selectedYear, out int selectedMonth)
    {
        selectedYear = 0;
        selectedMonth = 0;
        if (!int.TryParse(this.year.Text, out selectedYear) || selectedYear is < 2000 or > 9999)
        {
            this.status.Text = "Enter a valid year.";
            return false;
        }
        if (!int.TryParse(this.month.Text, out selectedMonth) || selectedMonth is < 1 or > 12)
        {
            this.status.Text = "Enter a month from 1 through 12.";
            return false;
        }
        return true;
    }

    private async Task LoadReportAsync()
    {
        if (!TryPeriod(out int selectedYear, out int selectedMonth)) return;

        ToggleBusy(true);
        try
        {
            PayrollDeductionReportSnapshot report = await this.client.GetPayrollReportAsync(selectedYear, selectedMonth);
            this.grid.ItemsSource = new ObservableCollection<PayrollDeductionDetailSnapshot>(report.Details);
            this.summary.Text =
                $"{report.Year:D4}-{report.Month:D2}  |  Employees with amount due: {report.EmployeesWithAmountDue:N0}  |  Total: {report.TotalEmployeeAmountDue:C}";
            this.send.IsEnabled = true;
            this.status.Text = "Report loaded.";
        }
        catch (Exception exception)
        {
            this.grid.ItemsSource = null;
            this.summary.Text = string.Empty;
            this.status.Text = $"Could not load report: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task SendReportAsync()
    {
        if (!TryPeriod(out int selectedYear, out int selectedMonth)) return;

        ToggleBusy(true);
        try
        {
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
        this.load.IsEnabled = !busy;
        if (busy) this.send.IsEnabled = false;
    }
}
