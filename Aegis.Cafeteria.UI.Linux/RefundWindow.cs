using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Presentation;
using Aegis.Cafeteria.Domain.Orders;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class RefundWindow : Window
{
    private readonly Order order;
    private readonly List<RowModel> rows = [];
    private readonly TextBox accountAmount = new() { Text = "0.00" };
    private readonly TextBlock total = new() { FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock credit = new() { FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock cash = new() { FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock remainingTender = new();
    private readonly StackPanel itemPanel = new() { Spacing = 6, MinHeight = 185 };

    public RefundWindow(Order order)
    {
        this.order = order ?? throw new ArgumentNullException(nameof(order));
        Title = "Refund Sale";
        Width = 760;
        Height = 620;
        MinWidth = 700;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        foreach (var item in order.Items)
        {
            var refunded = order.GetRefundedQuantity(item.Id);
            var remaining = item.Quantity - refunded;
            if (remaining > 0)
                rows.Add(new RowModel(item.Id, item.ProductName, item.UnitPrice, item.Quantity, refunded, remaining));
        }

        var root = new Grid { Margin = new Thickness(18), RowSpacing = 10 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(new TextBlock { Text = "Enter the quantity to refund for each item. Use Refund All Remaining for a full refund.", FontWeight = Avalonia.Media.FontWeight.Bold, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        BuildItems();
        var scroll = new ScrollViewer { Content = itemPanel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);

        var all = new Button { Content = "Refund All Remaining", MinWidth = 170 };
        all.Click += (_, _) => { foreach (var row in rows) { row.RefundQuantity = row.RemainingQuantity; row.QuantityBox.Text = row.RemainingQuantity.ToString(); } UpdateSummary(true); };
        Grid.SetRow(all, 2); root.Children.Add(all);

        var tender = new Grid { ColumnSpacing = 10, RowSpacing = 6 };
        tender.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        tender.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        tender.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        tender.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        AddTender(tender, 0, 0, "Refund Total", total);
        AddTender(tender, 0, 2, "Employee Card", accountAmount);
        AddTender(tender, 1, 2, "Employee Credit Reduction", credit);
        AddTender(tender, 2, 2, "Cash Refund", cash);
        Grid.SetRow(remainingTender, 1); Grid.SetColumn(remainingTender, 0); Grid.SetColumnSpan(remainingTender, 2); tender.Children.Add(remainingTender);
        accountAmount.TextChanged += (_, _) => UpdateSummary(false);
        Grid.SetRow(tender, 3); root.Children.Add(tender);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        var accept = new Button { Content = "Continue", MinWidth = 100 };
        cancel.Click += (_, _) => Close(false);
        accept.Click += (_, _) => { if (ValidateSelection()) Close(true); };
        buttons.Children.Add(cancel); buttons.Children.Add(accept);
        Grid.SetRow(buttons, 4); root.Children.Add(buttons);

        Content = root;
        UpdateSummary(true);
    }

    public IReadOnlyList<(Guid OrderItemId, int Quantity)> SelectedItems => rows.Where(x => x.RefundQuantity > 0).Select(x => (x.OrderItemId, x.RefundQuantity)).ToArray();
    public decimal RefundTotal => rows.Sum(x => x.UnitPrice * x.RefundQuantity);
    public decimal CafeteriaAccountAmount => decimal.TryParse(accountAmount.Text, out var value) ? value : 0m;
    private RefundQuote CurrentQuote => RefundQuoteCalculator.Calculate(order, SelectedItems, CafeteriaAccountAmount);
    public decimal CreditAmount => CurrentQuote.CreditAmount;
    public decimal CashAmount => CurrentQuote.CashAmount;

    private void BuildItems()
    {
        foreach (var row in rows)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(90)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(80)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(90)));
            grid.Children.Add(new TextBlock { Text = row.ProductName, VerticalAlignment = VerticalAlignment.Center });
            var price = new TextBlock { Text = CafeteriaDisplay.Money(row.UnitPrice), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(price, 1); grid.Children.Add(price);
            var available = new TextBlock { Text = $"Avail {row.RemainingQuantity}", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(available, 2); grid.Children.Add(available);
            row.QuantityBox.PlaceholderText = "Qty";
            row.QuantityBox.Text = "0";
            row.QuantityBox.TextChanged += (_, _) => { if (int.TryParse(row.QuantityBox.Text, out var qty) && qty >= 0 && qty <= row.RemainingQuantity) row.RefundQuantity = qty; UpdateSummary(true); };
            Grid.SetColumn(row.QuantityBox, 3); grid.Children.Add(row.QuantityBox);
            itemPanel.Children.Add(grid);
        }
    }

    private void UpdateSummary(bool resetAccountDefault)
    {
        RefundQuote quote = RefundQuoteCalculator.Calculate
        (
              order
            , SelectedItems
            , resetAccountDefault ? null : CafeteriaAccountAmount
        );

        accountAmount.Text = quote.CafeteriaAccountAmount.ToString("0.00");
        total.Text = CafeteriaDisplay.Money(quote.RefundTotal);
        credit.Text = CafeteriaDisplay.Money(quote.CreditAmount);
        cash.Text = CafeteriaDisplay.Money(quote.CashAmount);
        remainingTender.Text =
            CafeteriaDisplayText.RemainingOriginalTender(quote.RemainingCafeteriaAccount, quote.RemainingCredit, quote.RemainingCash);
    }

    private bool ValidateSelection()
    {
        RefundQuote quote = RefundQuoteCalculator.Calculate(order, SelectedItems, CafeteriaAccountAmount);
        if (quote.IsValid)
        {
            return true;
        }

        remainingTender.Text = quote.ValidationMessage ?? "Refund selection is invalid.";
        return false;
    }

    private static void AddTender(Grid grid, int row, int column, string caption, Control value)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var label = new TextBlock { Text = caption, FontWeight = Avalonia.Media.FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row); Grid.SetColumn(label, column); grid.Children.Add(label);
        Grid.SetRow(value, row); Grid.SetColumn(value, column + 1); grid.Children.Add(value);
    }

    private sealed class RowModel(Guid orderItemId, string productName, decimal unitPrice, int purchasedQuantity, int refundedQuantity, int remainingQuantity)
    {
        public Guid OrderItemId { get; } = orderItemId;
        public string ProductName { get; } = productName;
        public decimal UnitPrice { get; } = unitPrice;
        public int PurchasedQuantity { get; } = purchasedQuantity;
        public int RefundedQuantity { get; } = refundedQuantity;
        public int RemainingQuantity { get; } = remainingQuantity;
        public int RefundQuantity { get; set; }
        public TextBox QuantityBox { get; } = new();
    }
}

