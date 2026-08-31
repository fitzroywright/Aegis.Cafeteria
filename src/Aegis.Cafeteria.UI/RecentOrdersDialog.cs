using Aegis.Cafeteria.Domain.Orders;

namespace Aegis.Cafeteria.UI;

public sealed class RecentOrdersDialog : Form
{
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        RowHeadersVisible = false
    };
    public Order? SelectedOrder { get; private set; }

    public RecentOrdersDialog(IReadOnlyList<Order> orders, string title = "Recent Completed Sales")
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 480);
        Font = new Font("Segoe UI", 10);

        // The refund chooser is intentionally capped at five orders; make all five
        // rows visible without forcing the cashier to scroll.
        grid.RowTemplate.Height = 28;
        grid.MinimumSize = new Size(0, grid.ColumnHeadersHeight + (5 * grid.RowTemplate.Height) + 4);

        var rows = orders.Select(x => new OrderRowView(x)).ToList();
        grid.DataSource = rows;
        if (grid.Columns[nameof(OrderRowView.Order)] is DataGridViewColumn hidden) hidden.Visible = false;

        var select = new Button { Text = "Select", Width = 100, Height = 34 };
        var cancel = new Button { Text = "Cancel", Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(select);
        buttons.Controls.Add(cancel);
        Controls.Add(grid);
        Controls.Add(buttons);
        AcceptButton = select;
        CancelButton = cancel;

        select.Click += (_, _) => Choose();
        grid.CellDoubleClick += (_, _) => Choose();
    }

    private void Choose()
    {
        if (grid.CurrentRow?.DataBoundItem is not OrderRowView row) return;
        SelectedOrder = row.Order;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record OrderRowView(Order Order)
    {
        public DateTime Completed => Order.CompletedAt?.LocalDateTime ?? DateTime.MinValue;
        public decimal Total => Order.Total;
        public decimal Account => Order.CafeteriaAccountPaid;
        public decimal Cash => Order.CashPaid;
        public decimal Refunded => Order.RefundedAmount;
        public string Cashier => Order.CashierUserId ?? string.Empty;
        public string Terminal => Order.TerminalId ?? string.Empty;
        public string Transaction => Order.Id.ToString();
    }
}
