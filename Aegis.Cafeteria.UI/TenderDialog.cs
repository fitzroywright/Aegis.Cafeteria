namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Orders;

internal sealed class TenderDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Orange = Color.FromArgb(235, 139, 40);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);

    private readonly NumericUpDown account = new()
    {
        DecimalPlaces = 2,
        Maximum = 100000000,
        Minimum = 0,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill
    };

    private readonly NumericUpDown cashReceived = new()
    {
        DecimalPlaces = 2,
        Maximum = 100000000,
        Minimum = 0,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill
    };

    private readonly CheckBox useCredit = new()
    {
        Text = "Put the uncovered balance on Employee Credit",
        AutoSize = true,
        Checked = false,
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        ForeColor = Color.FromArgb(142, 76, 8)
    };

    private readonly Label credit = ValueLabel();
    private readonly Label remaining = ValueLabel();
    private readonly Label change = ValueLabel();
    private readonly Label newResponsibility = ValueLabel();
    private readonly Button complete = new()
    {
        Text = "Complete Sale",
        Width = 125,
        Height = 38,
        BackColor = Green,
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        DialogResult = DialogResult.None
    };

    private readonly decimal total;
    private readonly decimal subsidyRemaining;
    private readonly decimal maximumAccountAmount;
    private readonly bool creditSalesAllowed;
    private bool updatingTender;

    public decimal CafeteriaAccountAmount => CurrentEditorValue(this.account);
    public decimal CashReceived => CurrentEditorValue(this.cashReceived);
    private TenderQuote CurrentQuote => TenderQuoteCalculator.Calculate
    (
          this.total
        , this.maximumAccountAmount
        , this.subsidyRemaining
        , cafeteriaAccountAllowed: this.maximumAccountAmount > 0m
        , creditSalesAllowed: this.creditSalesAllowed
        , cafeteriaAccountAmount: CafeteriaAccountAmount
        , cashReceived: CashReceived
        , useEmployeeCredit: this.useCredit.Checked
    );

    public decimal CashAmount => CurrentQuote.CashApplied;
    public decimal CreditAmount => CurrentQuote.CreditAmount;
    public decimal RemainingAmount => CurrentQuote.RemainingAmount;
    public decimal ChangeAmount => CurrentQuote.ChangeAmount;

    public TenderDialog(decimal total, decimal availableCredit, decimal subsidyRemaining)
        : this(new TenderDialogOptions(total, availableCredit, subsidyRemaining, false))
    {
    }

    public TenderDialog(TenderDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        this.total = options.Total;
        this.subsidyRemaining = Math.Max(0m, options.SubsidyRemaining);
        this.creditSalesAllowed = options.CreditSalesAllowed;
        this.maximumAccountAmount = Math.Min(this.total, Math.Max(0m, options.AvailableCredit));

        Text = "Complete Cafeteria Sale";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 540);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.White;

        this.account.Maximum = this.maximumAccountAmount;
        this.account.Value = this.maximumAccountAmount;
        this.account.Enabled = this.maximumAccountAmount > 0m;
        this.cashReceived.Value = 0m;
        this.useCredit.Enabled = this.creditSalesAllowed;

        // Employee Credit is always an explicit cashier choice. The application
        // must never select this checkbox based on available credit, sale total,
        // uncovered balance, or any other calculation.
        this.useCredit.Checked = false;

        this.account.ValueChanged += (_, _) => UpdatePaymentState();
        this.cashReceived.ValueChanged += (_, _) => UpdateAccountFromCash();
        this.account.TextChanged += (_, _) => UpdatePaymentState();
        this.cashReceived.TextChanged += (_, _) => UpdateAccountFromCash();
        this.useCredit.CheckedChanged += (_, _) => UpdatePaymentState();
        this.complete.Click += (_, _) =>
        {
            if (!TryCurrentEditorValue(this.account, out decimal accountAmount) ||
                !TryCurrentEditorValue(this.cashReceived, out decimal cashReceived) ||
                !TenderQuoteCalculator.Calculate
                (
                      this.total
                    , this.maximumAccountAmount
                    , this.subsidyRemaining
                    , cafeteriaAccountAllowed: this.maximumAccountAmount > 0m
                    , creditSalesAllowed: this.creditSalesAllowed
                    , cafeteriaAccountAmount: accountAmount
                    , cashReceived: cashReceived
                    , useEmployeeCredit: this.useCredit.Checked
                ).IsComplete)
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(BuildLayout(options.AvailableCredit));
        CancelButton = Controls.Find("cancelButton", true).OfType<Button>().Single();
        UpdatePaymentState();
        Shown += (_, _) => this.cashReceived.Focus();
    }

    private Control BuildLayout(decimal availableCredit)
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(22)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));

        Label heading = new()
        {
            Text = "PAYMENT",
            AutoSize = true,
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Navy,
            Margin = new Padding(0, 0, 0, 12)
        };
        table.Controls.Add(heading, 0, 0);
        table.SetColumnSpan(heading, 2);

        table.Controls.Add(Caption("Order Total"), 0, 1);
        table.Controls.Add(ValueLabel(this.total.ToString("C2")), 1, 1);
        table.Controls.Add(Caption("Available to Spend"), 0, 2);
        table.Controls.Add(ValueLabel(availableCredit.ToString("C2"), Blue), 1, 2);
        table.Controls.Add(Caption("Employee Card"), 0, 3);
        table.Controls.Add(this.account, 1, 3);
        table.Controls.Add(Caption("Cash Received"), 0, 4);
        table.Controls.Add(this.cashReceived, 1, 4);

        Panel creditPanel = new()
        {
            Dock = DockStyle.Fill,
            Height = 82,
            BackColor = Color.FromArgb(255, 246, 232),
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 10, 0, 10)
        };
        TableLayoutPanel creditLayout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        creditLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        creditLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        this.credit.ForeColor = Orange;
        creditLayout.Controls.Add(this.useCredit, 0, 0);
        creditLayout.Controls.Add(this.credit, 1, 0);
        Label creditHint = new()
        {
            Text = this.creditSalesAllowed
                ? "Credit becomes Employee Amount Due. If unpaid at month-end it is carried into the next period."
                : "Employee Credit is not available for this customer.",
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5f)
        };
        creditLayout.Controls.Add(creditHint, 0, 1);
        creditLayout.SetColumnSpan(creditHint, 2);
        creditPanel.Controls.Add(creditLayout);
        table.Controls.Add(creditPanel, 0, 5);
        table.SetColumnSpan(creditPanel, 2);

        table.Controls.Add(Caption("New Employee Responsibility"), 0, 6);
        table.Controls.Add(this.newResponsibility, 1, 6);
        table.Controls.Add(Caption("Remaining"), 0, 7);
        table.Controls.Add(this.remaining, 1, 7);
        table.Controls.Add(Caption("Change Due"), 0, 8);
        table.Controls.Add(this.change, 1, 8);

        FlowLayoutPanel buttons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 10, 0, 0)
        };
        Button cancel = new()
        {
            Name = "cancelButton",
            Text = "Cancel",
            Width = 100,
            Height = 38,
            DialogResult = DialogResult.Cancel
        };
        buttons.Controls.Add(this.complete);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 9);
        table.SetColumnSpan(buttons, 2);

        return table;
    }

    private void UpdateAccountFromCash()
    {
        if (this.updatingTender)
            return;

        if (!TryCurrentEditorValue(this.cashReceived, out decimal cashAmount))
        {
            UpdatePaymentState();
            return;
        }

        decimal accountAmount = TenderAllocationCalculator.CalculateCafeteriaAccountAmount(
            this.total,
            this.maximumAccountAmount,
            cashAmount);

        this.updatingTender = true;
        try
        {
            this.account.Value = accountAmount;
        }
        finally
        {
            this.updatingTender = false;
        }

        UpdatePaymentState();
    }

    private void UpdatePaymentState()
    {
        bool valid = TryCurrentEditorValue(this.account, out _) && TryCurrentEditorValue(this.cashReceived, out _);
        if (!valid)
        {
            this.credit.Text = "—";
            this.remaining.Text = "—";
            this.change.Text = "—";
            this.newResponsibility.Text = "—";
            this.complete.Enabled = false;
            return;
        }

        TenderQuote quote = CurrentQuote;

        this.credit.Text = quote.CreditAmount.ToString("C2");
        this.credit.ForeColor = quote.CreditAmount > 0m ? Orange : Muted;
        this.remaining.Text = quote.RemainingAmount.ToString("C2");
        this.change.Text = quote.ChangeAmount.ToString("C2");
        this.newResponsibility.Text = quote.NewCustomerResponsibility.ToString("C2");
        this.newResponsibility.ForeColor = quote.NewCustomerResponsibility > 0m ? Orange : Green;
        this.remaining.ForeColor = quote.RemainingAmount > 0m ? Color.Firebrick : Green;
        this.change.ForeColor = quote.ChangeAmount > 0m ? Green : SystemColors.ControlText;
        this.complete.Enabled = quote.IsComplete;
    }

    private static decimal CurrentEditorValue(NumericUpDown control)
        => TryCurrentEditorValue(control, out decimal value) ? value : control.Value;

    private static bool TryCurrentEditorValue(NumericUpDown control, out decimal value)
    {
        string? text = control.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            value = 0m;
            return true;
        }

        if (!decimal.TryParse(
            text,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.CurrentCulture,
            out value))
        {
            return false;
        }

        return value >= control.Minimum && value <= control.Maximum;
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        ForeColor = Navy,
        Margin = new Padding(0, 7, 0, 7)
    };

    private static Label ValueLabel(string text = "", Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 12, FontStyle.Bold),
        ForeColor = color ?? Navy,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 0, 7)
    };
}

internal sealed record TenderDialogOptions(
    decimal Total,
    decimal AvailableCredit,
    decimal SubsidyRemaining,
    bool CreditSalesAllowed);
