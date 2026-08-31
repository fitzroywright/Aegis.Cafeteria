using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class CustomerPaymentWindow : Window
{
    private readonly TextBox amount = new() { FontSize = 16, FontWeight = FontWeight.Bold };
    private readonly ComboBox method = new();
    private readonly TextBox reference = new() { Watermark = "Optional receipt / POS / bank reference" };
    private readonly TextBlock remaining = new() { FontWeight = FontWeight.Bold, FontSize = 16 };
    private readonly decimal amountDue;

    public CustomerPaymentWindow(string customerName, decimal amountDue)
    {
        this.amountDue = amountDue;
        Title = "Customer Payment";
        Width = 590;
        Height = 470;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        amount.Text = amountDue.ToString("0.00");
        amount.TextChanged += (_, _) => UpdateRemaining();
        method.ItemsSource = new[] { "Cash", "Card / POS", "Bank Transfer", "Cheque", "Other" };
        method.SelectedIndex = 0;

        var grid = new Grid { Margin = new Thickness(22), RowSpacing = 10, ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.44, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.56, GridUnitType.Star)));
        for (var i = 0; i < 9; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        var title = new TextBlock { Text = "CUSTOMER PAYMENT", FontSize = 20, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#182D44")) };
        Grid.SetColumnSpan(title, 2); grid.Children.Add(title);
        var subtitle = new TextBlock { Text = "Record money received from the employee. This reduces the Payroll Deduction for the current period.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        Grid.SetRow(subtitle, 1); Grid.SetColumnSpan(subtitle, 2); grid.Children.Add(subtitle);

        AddRow(grid, 2, "Customer", new TextBlock { Text = customerName, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Bold });
        AddRow(grid, 3, "Payroll Deduction before payment", new TextBlock { Text = Money(amountDue), FontWeight = FontWeight.Bold, FontSize = 17 });
        AddRow(grid, 4, "Payment amount", amount);
        AddRow(grid, 5, "Payment method", method);
        AddRow(grid, 6, "Reference", reference);
        AddRow(grid, 7, "Payroll Deduction after payment", remaining);

        var note = new TextBlock { Text = "The payment is recorded against this employee and reduces the amount expected from payroll. It does not create a new sale.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gray };
        Grid.SetRow(note, 8); Grid.SetColumnSpan(note, 2); grid.Children.Add(note);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = "Cancel", MinWidth = 90, MinHeight = 40 };
        var save = new Button { Content = "Apply Payment", MinWidth = 135, MinHeight = 40, Background = new SolidColorBrush(Color.Parse("#17774C")), Foreground = Brushes.White };
        cancel.Click += (_, _) => Close(false);
        save.Click += (_, _) =>
        {
            if (!TryGetAmount(out var entered) || entered <= 0m || entered > amountDue)
            {
                remaining.Text = $"Enter an amount from {Money(0.01m)} through {Money(amountDue)}.";
                return;
            }
            Close(true);
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        Grid.SetRow(buttons, 9); Grid.SetColumnSpan(buttons, 2); grid.Children.Add(buttons);

        Content = grid;
        UpdateRemaining();
    }

    public decimal PaymentAmount => TryGetAmount(out var value) ? value : 0m;
    public string PaymentMethod => method.SelectedItem?.ToString() ?? "Cash";
    public string? Reference => string.IsNullOrWhiteSpace(reference.Text) ? null : reference.Text.Trim();

    private void UpdateRemaining()
    {
        var entered = TryGetAmount(out var value) ? value : 0m;
        remaining.Text = Money(Math.Max(0m, amountDue - entered));
    }

    private bool TryGetAmount(out decimal value) => decimal.TryParse(amount.Text, out value);
    private static string Money(decimal value) => value.ToString("C2");

    private static void AddRow(Grid grid, int row, string caption, Control value)
    {
        var label = new TextBlock { Text = caption, FontWeight = FontWeight.Bold, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row); grid.Children.Add(label);
        Grid.SetRow(value, row); Grid.SetColumn(value, 1); grid.Children.Add(value);
    }
}
