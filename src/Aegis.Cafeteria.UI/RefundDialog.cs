namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Domain.Orders;

internal sealed class RefundDialog : Form
{
    private readonly Order order;
    private readonly DataGridView items = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect
    };
    private readonly NumericUpDown account = new()
    {
        DecimalPlaces = 2,
        Maximum = 100000000m,
        Minimum = 0m,
        ThousandsSeparator = true,
        Width = 150
    };
    private readonly Label credit = ValueLabel();
    private readonly Label cash = ValueLabel();
    private readonly Label total = ValueLabel();
    private readonly Label remainingTender = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button refund = new() { Text = "Continue", Width = 100, Height = 34, Enabled = false };
    private readonly List<RowModel> rows = [];
    private bool updating;

    public IReadOnlyList<(Guid OrderItemId, int Quantity)> SelectedItems =>
        this.rows.Where(x => x.RefundQuantity > 0).Select(x => (x.OrderItemId, x.RefundQuantity)).ToArray();

    public decimal CafeteriaAccountAmount => this.account.Value;
    public decimal CreditAmount
    {
        get
        {
            decimal remainingAfterAccount = Math.Max(0m, RefundTotal - CafeteriaAccountAmount);
            decimal remainingCredit = Math.Max(0m, this.order.CreditPaid - this.order.CreditRefunded);
            return Math.Min(remainingAfterAccount, remainingCredit);
        }
    }
    public decimal CashAmount => Math.Max(0m, RefundTotal - CafeteriaAccountAmount - CreditAmount);
    public decimal RefundTotal => this.rows.Sum(x => x.UnitPrice * x.RefundQuantity);

    public RefundDialog(Order order)
    {
        this.order = order ?? throw new ArgumentNullException(nameof(order));
        Text = "Refund Sale";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(800, 590);
        MinimumSize = new Size(740, 560);
        Font = new Font("Segoe UI", 10);

        BuildRows();
        BuildGrid();

        this.items.RowTemplate.Height = 28;
        this.items.MinimumSize = new Size(0, this.items.ColumnHeadersHeight + (5 * this.items.RowTemplate.Height) + 4);

        Controls.Add(BuildLayout());

        this.items.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 4) SyncFromGrid();
        };
        this.items.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (this.items.IsCurrentCellDirty) this.items.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        this.items.CellValidating += ValidateQuantity;
        this.account.ValueChanged += (_, _) => { if (!this.updating) UpdateSummary(false); };
        this.refund.Click += (_, _) =>
        {
            SyncFromGrid();
            if (!ValidateSelection()) return;
            DialogResult = DialogResult.OK;
            Close();
        };

        Shown += (_, _) => UpdateSummary(true);
    }

    private void BuildRows()
    {
        foreach (OrderItem item in this.order.Items)
        {
            int refunded = this.order.GetRefundedQuantity(item.Id);
            int remaining = item.Quantity - refunded;
            if (remaining > 0)
                this.rows.Add(new RowModel(item.Id, item.ProductName, item.UnitPrice, item.Quantity, refunded, remaining));
        }
    }

    private void BuildGrid()
    {
        this.items.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item", DataPropertyName = nameof(RowModel.ProductName), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        this.items.Columns.Add(NumericColumn("Price", nameof(RowModel.UnitPrice), 95, "C2", true));
        this.items.Columns.Add(NumericColumn("Bought", nameof(RowModel.PurchasedQuantity), 70, null, true));
        this.items.Columns.Add(NumericColumn("Available", nameof(RowModel.RemainingQuantity), 80, null, true));
        this.items.Columns.Add(NumericColumn("Refund Qty", nameof(RowModel.RefundQuantity), 90, null, false));
        this.items.DataSource = this.rows;
    }

    private static DataGridViewTextBoxColumn NumericColumn(string header, string property, int width, string? format, bool readOnly)
    {
        DataGridViewCellStyle style = new() { Alignment = DataGridViewContentAlignment.MiddleRight };
        if (!string.IsNullOrWhiteSpace(format)) style.Format = format;
        return new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            ReadOnly = readOnly,
            Width = width,
            DefaultCellStyle = style,
            HeaderCell = { Style = { Alignment = DataGridViewContentAlignment.MiddleRight } }
        };
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label { Text = "Enter the quantity to refund for each item. Employee Credit is reduced before any cash is paid out.", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        root.Controls.Add(this.items, 0, 1);

        Button all = new() { Text = "Refund All Remaining", Width = 170, Height = 32, Margin = new Padding(0, 10, 0, 8) };
        all.Click += (_, _) =>
        {
            foreach (RowModel row in this.rows) row.RefundQuantity = row.RemainingQuantity;
            this.items.Refresh();
            UpdateSummary(true);
        };
        root.Controls.Add(all, 0, 2);

        TableLayoutPanel tender = new() { AutoSize = true, ColumnCount = 4, RowCount = 3, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
        tender.Controls.Add(Caption("Refund Total"), 0, 0);
        tender.Controls.Add(this.total, 1, 0);
        tender.Controls.Add(Caption("Employee Card"), 2, 0);
        tender.Controls.Add(this.account, 3, 0);
        tender.Controls.Add(Caption("Employee Credit Reduction"), 2, 1);
        tender.Controls.Add(this.credit, 3, 1);
        tender.Controls.Add(Caption("Cash Refund"), 2, 2);
        tender.Controls.Add(this.cash, 3, 2);
        tender.Controls.Add(this.remainingTender, 0, 1);
        tender.SetColumnSpan(this.remainingTender, 2);
        root.Controls.Add(tender, 0, 3);

        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        Button cancel = new() { Text = "Cancel", Width = 90, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(this.refund);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 4);
        CancelButton = cancel;
        AcceptButton = this.refund;
        return root;
    }

    private void SyncFromGrid()
    {
        this.items.EndEdit();
        UpdateSummary(true);
    }

    private void ValidateQuantity(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 4) return;
        RowModel row = this.rows[e.RowIndex];
        if (!int.TryParse(Convert.ToString(e.FormattedValue), out int quantity) || quantity < 0 || quantity > row.RemainingQuantity)
        {
            e.Cancel = true;
            MessageBox.Show(this, $"Refund quantity must be between 0 and {row.RemainingQuantity}.", "Refund", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateSummary(bool resetAccountDefault)
    {
        this.updating = true;
        try
        {
            decimal refundTotal = RefundTotal;
            decimal remainingAccount = Math.Max(0m, this.order.CafeteriaAccountPaid - this.order.CafeteriaAccountRefunded);
            decimal remainingCash = Math.Max(0m, this.order.CashPaid - this.order.CashRefunded);
            decimal remainingCredit = Math.Max(0m, this.order.CreditPaid - this.order.CreditRefunded);
            decimal accountMin = Math.Max(0m, refundTotal - remainingCredit - remainingCash);
            decimal accountMax = Math.Min(refundTotal, remainingAccount);

            this.account.Minimum = accountMin;
            this.account.Maximum = Math.Max(accountMin, accountMax);
            if (resetAccountDefault)
                this.account.Value = Math.Min(this.account.Maximum, Math.Max(this.account.Minimum, Math.Min(refundTotal, remainingAccount)));
            else if (this.account.Value < this.account.Minimum)
                this.account.Value = this.account.Minimum;
            else if (this.account.Value > this.account.Maximum)
                this.account.Value = this.account.Maximum;

            this.total.Text = refundTotal.ToString("C2");
            this.credit.Text = CreditAmount.ToString("C2");
            this.credit.ForeColor = CreditAmount > 0m ? Color.DarkOrange : SystemColors.ControlText;
            this.cash.Text = CashAmount.ToString("C2");
            this.remainingTender.Text = $"Remaining original tender: Employee Card {remainingAccount:C2}, Credit {remainingCredit:C2}, Cash {remainingCash:C2}";
            this.refund.Enabled = refundTotal > 0m && refundTotal <= remainingAccount + remainingCredit + remainingCash;
        }
        finally
        {
            this.updating = false;
        }
    }

    private bool ValidateSelection()
    {
        IReadOnlyList<(Guid OrderItemId, int Quantity)> selected = SelectedItems;
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select at least one item quantity to refund.", "Refund", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        decimal remainingAccount = this.order.CafeteriaAccountPaid - this.order.CafeteriaAccountRefunded;
        decimal remainingCredit = this.order.CreditPaid - this.order.CreditRefunded;
        decimal remainingCash = this.order.CashPaid - this.order.CashRefunded;
        if (CafeteriaAccountAmount > remainingAccount || CreditAmount > remainingCredit || CashAmount > remainingCash || CafeteriaAccountAmount + CreditAmount + CashAmount != RefundTotal)
        {
            MessageBox.Show(this, "Refund allocation must match the selected total and cannot exceed the original remaining Employee Card, Credit, or Cash tender.", "Refund", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 10, 7) };
    private static Label ValueLabel() => new() { AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 20, 7) };

    private sealed class RowModel(Guid orderItemId, string productName, decimal unitPrice, int purchasedQuantity, int refundedQuantity, int remainingQuantity)
    {
        public Guid OrderItemId { get; } = orderItemId;
        public string ProductName { get; } = productName;
        public decimal UnitPrice { get; } = unitPrice;
        public int PurchasedQuantity { get; } = purchasedQuantity;
        public int RefundedQuantity { get; } = refundedQuantity;
        public int RemainingQuantity { get; } = remainingQuantity;
        public int RefundQuantity { get; set; }
    }
}
