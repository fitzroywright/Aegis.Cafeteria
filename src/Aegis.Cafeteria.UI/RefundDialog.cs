namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Domain.Orders;

internal sealed class RefundDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Red = Color.FromArgb(176, 52, 52);
    private static readonly Color Orange = Color.FromArgb(202, 111, 26);

    private readonly Order order;
    private readonly DataGridView items = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        GridColor = Color.FromArgb(224, 229, 234),
        EnableHeadersVisualStyles = false,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(236, 241, 246),
            ForeColor = Navy,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            SelectionBackColor = Color.FromArgb(236, 241, 246),
            SelectionForeColor = Navy
        }
    };
    private readonly NumericUpDown account = new()
    {
        DecimalPlaces = 2,
        Maximum = 100000000m,
        Minimum = 0m,
        ThousandsSeparator = true,
        Width = 150,
        Font = new Font("Segoe UI", 10.5F)
    };
    private readonly Label credit = ValueLabel();
    private readonly Label cash = ValueLabel();
    private readonly Label total = ValueLabel(Red);
    private readonly Label remainingTender = new()
    {
        AutoSize = true,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 9.5F)
    };
    private readonly Button refund = ActionButton("Continue", Green, 110);
    private readonly List<RowModel> rows = [];
    private bool updating;

    public IReadOnlyList<(Guid OrderItemId, int Quantity)> SelectedItems =>
        this.rows.Where(row => row.RefundQuantity > 0).Select(row => (row.OrderItemId, row.RefundQuantity)).ToArray();

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
    public decimal RefundTotal => this.rows.Sum(row => row.UnitPrice * row.RefundQuantity);

    public RefundDialog(Order order)
    {
        this.order = order ?? throw new ArgumentNullException(nameof(order));
        Text = "Refund Sale";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(860, 640);
        MinimumSize = new Size(780, 600);
        Font = new Font("Segoe UI", 10);
        BackColor = Canvas;

        this.refund.Enabled = false;
        BuildRows();
        BuildGrid();

        this.items.RowTemplate.Height = 30;
        this.items.MinimumSize = new Size(0, this.items.ColumnHeadersHeight + (5 * this.items.RowTemplate.Height) + 4);

        Controls.Add(BuildBody());
        Controls.Add(BuildHeader());

        this.items.CellValueChanged += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex >= 0 && eventArgs.ColumnIndex == 4)
            {
                SyncFromGrid();
            }
        };
        this.items.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (this.items.IsCurrentCellDirty)
            {
                this.items.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        this.items.CellValidating += ValidateQuantity;
        this.account.ValueChanged += (_, _) =>
        {
            if (!this.updating)
            {
                UpdateSummary(false);
            }
        };
        this.refund.Click += (_, _) =>
        {
            SyncFromGrid();
            if (!ValidateSelection())
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        Shown += (_, _) => UpdateSummary(true);
    }

    private Control BuildHeader()
    {
        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Navy,
            Padding = new Padding(24, 14, 24, 12)
        };
        FlowLayoutPanel stack = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        stack.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "REFUND SALE",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold)
        });
        stack.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Select quantities and confirm how the refund should be returned.",
            ForeColor = Color.FromArgb(193, 210, 225),
            Font = new Font("Segoe UI", 9.5F)
        });
        header.Controls.Add(stack);
        return header;
    }

    private Control BuildBody()
    {
        Panel card = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(24)
        };

        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Enter the quantity to refund for each item. Employee Credit is reduced before any cash is paid out.",
            AutoSize = true,
            ForeColor = Navy,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        root.Controls.Add(this.items, 0, 1);

        Button all = SecondaryButton("Refund All Remaining", 180);
        all.Margin = new Padding(0, 12, 0, 10);
        all.Click += (_, _) =>
        {
            foreach (RowModel row in this.rows)
            {
                row.RefundQuantity = row.RemainingQuantity;
            }

            this.items.Refresh();
            UpdateSummary(true);
        };
        root.Controls.Add(all, 0, 2);

        Panel tenderCard = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            BackColor = Color.FromArgb(246, 249, 252),
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 12)
        };
        TableLayoutPanel tender = new()
        {
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 3,
            Dock = DockStyle.Fill
        };
        tender.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tender.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        tender.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tender.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        tenderCard.Controls.Add(tender);
        root.Controls.Add(tenderCard, 0, 3);

        FlowLayoutPanel buttons = new()
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty
        };
        Button cancel = SecondaryButton("Cancel", 100);
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Margin = new Padding(8, 0, 0, 0);
        this.refund.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(this.refund);
        root.Controls.Add(buttons, 0, 4);
        CancelButton = cancel;
        AcceptButton = this.refund;

        card.Controls.Add(root);
        return card;
    }

    private void BuildRows()
    {
        foreach (OrderItem item in this.order.Items)
        {
            int refunded = this.order.GetRefundedQuantity(item.Id);
            int remaining = item.Quantity - refunded;
            if (remaining > 0)
            {
                this.rows.Add(new RowModel(item.Id, item.ProductName, item.UnitPrice, item.Quantity, refunded, remaining));
            }
        }
    }

    private void BuildGrid()
    {
        this.items.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Item",
            DataPropertyName = nameof(RowModel.ProductName),
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        this.items.Columns.Add(NumericColumn("Price", nameof(RowModel.UnitPrice), 95, "C2", true));
        this.items.Columns.Add(NumericColumn("Bought", nameof(RowModel.PurchasedQuantity), 70, null, true));
        this.items.Columns.Add(NumericColumn("Available", nameof(RowModel.RemainingQuantity), 80, null, true));
        this.items.Columns.Add(NumericColumn("Refund Qty", nameof(RowModel.RefundQuantity), 90, null, false));
        this.items.DataSource = this.rows;
    }

    private static DataGridViewTextBoxColumn NumericColumn(string header, string property, int width, string? format, bool readOnly)
    {
        DataGridViewCellStyle style = new()
        {
            Alignment = DataGridViewContentAlignment.MiddleRight
        };
        if (!string.IsNullOrWhiteSpace(format))
        {
            style.Format = format;
        }

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

    private void SyncFromGrid()
    {
        this.items.EndEdit();
        UpdateSummary(true);
    }

    private void ValidateQuantity(object? sender, DataGridViewCellValidatingEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != 4)
        {
            return;
        }

        RowModel row = this.rows[eventArgs.RowIndex];
        if (!int.TryParse(Convert.ToString(eventArgs.FormattedValue), out int quantity)
            || quantity < 0
            || quantity > row.RemainingQuantity)
        {
            eventArgs.Cancel = true;
            MessageBox.Show
            (
                this,
                $"Refund quantity must be between 0 and {row.RemainingQuantity}.",
                "Refund",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
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
            {
                this.account.Value = Math.Min(this.account.Maximum, Math.Max(this.account.Minimum, Math.Min(refundTotal, remainingAccount)));
            }
            else if (this.account.Value < this.account.Minimum)
            {
                this.account.Value = this.account.Minimum;
            }
            else if (this.account.Value > this.account.Maximum)
            {
                this.account.Value = this.account.Maximum;
            }

            this.total.Text = refundTotal.ToString("C2");
            this.credit.Text = CreditAmount.ToString("C2");
            this.credit.ForeColor = CreditAmount > 0m ? Orange : Navy;
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
        if (CafeteriaAccountAmount > remainingAccount
            || CreditAmount > remainingCredit
            || CashAmount > remainingCash
            || CafeteriaAccountAmount + CreditAmount + CashAmount != RefundTotal)
        {
            MessageBox.Show
            (
                this,
                "Refund allocation must match the selected total and cannot exceed the original remaining Employee Card, Credit, or Cash tender.",
                "Refund",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            return false;
        }

        return true;
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        ForeColor = Muted,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        Margin = new Padding(0, 7, 10, 7)
    };

    private static Label ValueLabel(Color? color = null) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
        ForeColor = color ?? Navy,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 20, 7)
    };

    private static Button ActionButton(string text, Color backColor, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 40,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Cursor = Cursors.Hand
    };

    private static Button SecondaryButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 40,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = Navy,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Cursor = Cursors.Hand
    };

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
