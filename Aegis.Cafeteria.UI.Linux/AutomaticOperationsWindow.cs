namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Infrastructure.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

public sealed class AutomaticOperationsWindow : Window
{
    private readonly CafeteriaServicesClient services;
    private readonly NumericUpDown year = new() { Minimum = 2000, Maximum = 9999, Width = 110 };
    private readonly NumericUpDown month = new() { Minimum = 1, Maximum = 12, Width = 90 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };

    public AutomaticOperationsWindow(CafeteriaServicesClient services)
    {
        this.services = services;

        DateTime previous = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        this.year.Value = previous.Year;
        this.month.Value = previous.Month;

        Title = "Cafeteria Automatic Operations";
        Width = 720;
        Height = 520;
        MinWidth = 680;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#F4F7FA"));
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        StackPanel root = new() { Margin = new Thickness(24), Spacing = 14 };
        root.Children.Add(new TextBlock
        {
              Text = "Automatic Operations"
            , FontSize = 24
            , FontWeight = FontWeight.Bold
            , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
        });
        root.Children.Add(new TextBlock
        {
              Text = "Exception controls for service-owned automatic jobs. Normal opening, closing and scheduled reporting remain automatic."
            , TextWrapping = TextWrapping.Wrap
            , Foreground = Brushes.Gray
        });

        StackPanel period = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
        period.Children.Add(new TextBlock { Text = "Selected month", VerticalAlignment = VerticalAlignment.Center });
        period.Children.Add(this.year);
        period.Children.Add(this.month);
        root.Children.Add(period);

        root.Children.Add(Section(
            "Month lifecycle",
            ("Retry Month Opening", RetryOpenAsync),
            ("Recalculate Selected Month", RecalculateAsync),
            ("Retry Month Close", RetryCloseAsync)));

        root.Children.Add(Section(
            "Scheduled reports",
            ("Resend Yesterday Daily Sales", ResendDailySalesAsync),
            ("Resend Previous Week Manager Summary", ResendWeeklyManagerAsync)));

        root.Children.Add(this.status);

        Button close = new() { Content = "Close", Width = 100, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        root.Children.Add(close);
        return root;
    }

    private Control Section(string title, params (string Caption, Func<Task> Action)[] actions)
    {
        StackPanel panel = new() { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
              Text = title
            , FontSize = 16
            , FontWeight = FontWeight.Bold
            , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
        });

        WrapPanel buttons = new() { Orientation = Orientation.Horizontal };
        foreach ((string caption, Func<Task> action) in actions)
        {
            Button button = new()
            {
                  Content = caption
                , Margin = new Thickness(0, 0, 10, 8)
                , MinWidth = 155
            };

            button.Click += async (_, _) =>
            {
                button.IsEnabled = false;
                try
                {
                    this.status.Foreground = Brushes.Gray;
                    this.status.Text = $"Running {caption}...";
                    await action();
                }
                catch (Exception exception)
                {
                    this.status.Foreground = Brushes.Firebrick;
                    this.status.Text = exception.GetBaseException().Message;
                }
                finally
                {
                    button.IsEnabled = true;
                }
            };
            buttons.Children.Add(button);
        }

        panel.Children.Add(buttons);
        return new Border
        {
              Background = Brushes.White
            , Padding = new Thickness(16)
            , CornerRadius = new CornerRadius(6)
            , Child = panel
        };
    }

    private (int Year, int Month) SelectedPeriod()
        => ((int)(this.year.Value ?? DateTime.Today.Year), (int)(this.month.Value ?? DateTime.Today.Month));

    private async Task RetryOpenAsync()
    {
        (int selectedYear, int selectedMonth) = SelectedPeriod();
        MonthOperationResult result = await this.services.RetryMonthOpeningAsync(selectedYear, selectedMonth);
        Success($"Month {selectedYear:D4}-{selectedMonth:D2} opening checked. New allocations created: {result.Created.GetValueOrDefault()}.");
    }

    private async Task RecalculateAsync()
    {
        (int selectedYear, int selectedMonth) = SelectedPeriod();
        MonthOperationResult result = await this.services.RecalculateMonthAsync(selectedYear, selectedMonth);
        Success($"Month recalculation completed. Updated: {result.Updated.GetValueOrDefault()}, unchanged: {result.Unchanged.GetValueOrDefault()}.");
    }

    private async Task RetryCloseAsync()
    {
        (int selectedYear, int selectedMonth) = SelectedPeriod();
        MonthOperationResult result = await this.services.RetryMonthCloseAsync(selectedYear, selectedMonth);
        Success(result.IsClosed == true
            ? $"Month {selectedYear:D4}-{selectedMonth:D2} is closed. Newly closed employees: {result.NewlyClosedEmployees.GetValueOrDefault()}."
            : $"Month {selectedYear:D4}-{selectedMonth:D2} is not yet fully closed.");
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
        this.status.Foreground = Brushes.Green;
        this.status.Text = message;
    }
}
