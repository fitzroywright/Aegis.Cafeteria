using Aegis.Cafeteria.Application.Reporting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Aegis.Cafeteria.UI.Linux;

/// <summary>Read-only second-monitor customer display. It intentionally omits private identifiers.</summary>
public sealed class CustomerDisplayWindow : Window
{
    private readonly Image photo = new() { Width = 210, Height = 220, Stretch = Stretch.Uniform };
    private readonly TextBlock customerName = new() { FontSize = 30, FontWeight = FontWeight.Bold };
    private readonly TextBlock eligibility = new() { FontSize = 15, FontWeight = FontWeight.Bold };
    private readonly TextBlock period = new() { FontSize = 14 };
    private readonly TextBlock available = ValueText();
    private readonly TextBlock subsidy = ValueText();
    private readonly TextBlock employeeFunded = ValueText();
    private readonly TextBlock debt = ValueText();
    private readonly TextBlock employeeAmountDue = ValueText();
    private readonly TextBlock historyTitle = new() { FontSize = 20, FontWeight = FontWeight.Bold };
    private readonly ListBox history = new();

    public CustomerDisplayWindow()
    {
        Title = "Cafeteria - Customer Display";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.Parse("#F4F7FA"));
        Content = BuildLayout();
    }

    public void PlaceOn(PixelRect workingArea, double scaling)
    {
        Position = workingArea.Position;
        Width = workingArea.Width / Math.Max(1d, scaling);
        Height = workingArea.Height / Math.Max(1d, scaling);
    }

    public void ShowCustomer(CustomerAccountView account)
    {
        customerName.Text = account.Customer.Name;
        eligibility.Text = account.Customer.IsCashCustomer ? "CASH CUSTOMER" : account.Customer.IsEligibleForCredit ? "ELIGIBLE FOR CAFETERIA CREDIT" : "CASH PURCHASES ONLY";
        period.Text = account.Customer.IsCashCustomer ? "Cash customer" : $"Current cafeteria period: {new DateTime(account.Period.Year, account.Period.Month, 1):MMMM yyyy}";
        if (account.Customer.IsEligibleForCredit && !account.Customer.IsCashCustomer)
        {
            available.Text = Money(account.FinancialStatus.TotalAvailableToSpend);
            subsidy.Text = Money(account.FinancialStatus.SubsidyAmountRemaining);
            employeeFunded.Text = Money(account.FinancialStatus.NonSubsidyAmountRemaining);
            debt.Text = Money(account.FinancialStatus.CarriedForwardDebt);
            employeeAmountDue.Text = Money(account.FinancialStatus.EmployeeAmountDue);
        }
        else
        {
            available.Text = subsidy.Text = employeeFunded.Text = debt.Text = employeeAmountDue.Text = "—";
        }
        SetPhoto(account.Customer.Photo);
        historyTitle.Text = $"Transactions — {new DateTime(account.Period.Year, account.Period.Month, 1):MMMM yyyy}";
        history.ItemsSource = account.Transactions.Select(x => new DisplayTransaction(x)).ToArray();
    }

    public void ClearCustomer()
    {
        history.ItemsSource = null;
        if (photo.Source is Bitmap old) old.Dispose();
        photo.Source = null;
        Hide();
    }

    private Control BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(40), RowSpacing = 24 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.Children.Add(new TextBlock { Text = "CAFETERIA", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#1F6FBE")) });

        var card = new Border { Background = Brushes.White, Padding = new Thickness(28), CornerRadius = new CornerRadius(8) };
        var identity = new Grid { ColumnSpacing = 28 };
        identity.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        identity.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        identity.Children.Add(photo);
        var details = new StackPanel { Spacing = 8 };
        details.Children.Add(customerName); details.Children.Add(eligibility); details.Children.Add(period);
        var finance = new Grid { ColumnSpacing = 28, RowSpacing = 6, Margin = new Thickness(0, 20, 0, 0) };
        for (var i = 0; i < 5; i++) finance.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        AddMetric(finance, 0, "AVAILABLE TO SPEND", available);
        AddMetric(finance, 1, "SUBSIDY REMAINING", subsidy);
        AddMetric(finance, 2, "EMPLOYEE-FUNDED REMAINING", employeeFunded);
        AddMetric(finance, 3, "DEBT BROUGHT FORWARD", debt);
        AddMetric(finance, 4, "EMPLOYEE AMOUNT DUE", employeeAmountDue);
        details.Children.Add(finance);
        Grid.SetColumn(details, 1); identity.Children.Add(details); card.Child = identity;
        Grid.SetRow(card, 1); root.Children.Add(card);

        var historyCard = new Border { Background = Brushes.White, Padding = new Thickness(24), CornerRadius = new CornerRadius(8) };
        var historyGrid = new Grid { RowSpacing = 10 };
        historyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        historyGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        historyGrid.Children.Add(historyTitle); Grid.SetRow(history, 1); historyGrid.Children.Add(history); historyCard.Child = historyGrid;
        Grid.SetRow(historyCard, 2); root.Children.Add(historyCard);
        return root;
    }

    private static void AddMetric(Grid grid, int column, string caption, TextBlock value)
    {
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock { Text = caption, FontSize = 12, FontWeight = FontWeight.Bold });
        stack.Children.Add(value);
        Grid.SetColumn(stack, column); grid.Children.Add(stack);
    }

    private void SetPhoto(byte[]? bytes)
    {
        if (photo.Source is Bitmap old) old.Dispose();
        photo.Source = null;
        if (bytes is null || bytes.Length == 0) return;
        try { using var stream = new MemoryStream(bytes); photo.Source = new Bitmap(stream); } catch { }
    }

    private static TextBlock ValueText() => new() { FontSize = 22, FontWeight = FontWeight.Bold };
    private static string Money(decimal value) => value.ToString("C2");

    private sealed record DisplayTransaction(CustomerMonthlyTransactionView Transaction)
    {
        public override string ToString() => $"{Transaction.OccurredAt.LocalDateTime:g} | {Transaction.Kind} | {Transaction.Description} | {Money(Transaction.Amount)}";
    }
}
