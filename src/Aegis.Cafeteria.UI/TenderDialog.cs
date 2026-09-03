namespace Aegis.Cafeteria.UI;

internal sealed class TenderDialog : Form
{
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

    private readonly Label remaining = ValueLabel();
    private readonly Label change = ValueLabel();
    private readonly Label newResponsibility = ValueLabel();
    private readonly Button complete = new() { Text = "Complete", Width = 100, DialogResult = DialogResult.None };
    private readonly decimal total;
    private readonly decimal subsidyRemaining;
    private readonly decimal maximumAccountAmount;

    public decimal CafeteriaAccountAmount => CurrentEditorValue(this.account);
    public decimal CashReceived => CurrentEditorValue(this.cashReceived);
    public decimal RemainingAmount => Math.Max(0m, this.total - CafeteriaAccountAmount - CashReceived);
    public decimal ChangeAmount => Math.Max(0m, CashReceived - Math.Max(0m, this.total - CafeteriaAccountAmount));
    public decimal CashAmount => Math.Min(CashReceived, Math.Max(0m, this.total - CafeteriaAccountAmount));

    public TenderDialog(decimal total, decimal availableCredit, decimal subsidyRemaining)
    {
        this.total = total;
        this.subsidyRemaining = Math.Max(0m, subsidyRemaining);
        Text = "Payment";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Size = new Size(500, 455);

        this.maximumAccountAmount = Math.Min(total, Math.Max(0m, availableCredit));
        this.account.Maximum = this.maximumAccountAmount;

        // Employee Credit is always opt-in. The cashier must ask the employee
        // and deliberately enter/select the amount to use for this sale.
        this.account.Value = 0m;
        this.account.Enabled = this.maximumAccountAmount > 0m;
        this.cashReceived.Value = 0m;

        this.account.ValueChanged += (_, _) => UpdatePaymentState();
        this.cashReceived.ValueChanged += (_, _) => UpdatePaymentState();
        // NumericUpDown does not always commit typed text to Value until focus
        // leaves the editor. Recalculate from the live editor text so Remaining,
        // Change and the Complete button update while the cashier is typing.
        this.account.TextChanged += (_, _) => UpdatePaymentState();
        this.cashReceived.TextChanged += (_, _) => UpdatePaymentState();
        this.complete.Click += (_, _) =>
        {
            if (!TryCurrentEditorValue(this.account, out decimal accountAmount) ||
                !TryCurrentEditorValue(this.cashReceived, out decimal cashReceived) ||
                Math.Max(0m, this.total - accountAmount - cashReceived) > 0m)
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(18)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        table.Controls.Add(Caption("Order Total"), 0, 0);
        table.Controls.Add(ValueLabel(total.ToString("C2")), 1, 0);

        table.Controls.Add(Caption("Available Employee Credit"), 0, 1);
        table.Controls.Add(ValueLabel(availableCredit.ToString("C2")), 1, 1);

        table.Controls.Add(Caption("Employee Credit"), 0, 2);
        table.Controls.Add(this.account, 1, 2);

        table.Controls.Add(Caption("New Customer Responsibility"), 0, 3);
        table.Controls.Add(this.newResponsibility, 1, 3);

        table.Controls.Add(Caption("Cash Received"), 0, 4);
        table.Controls.Add(this.cashReceived, 1, 4);

        table.Controls.Add(Caption("Remaining"), 0, 5);
        table.Controls.Add(this.remaining, 1, 5);

        table.Controls.Add(Caption("Change Due"), 0, 6);
        table.Controls.Add(this.change, 1, 6);

        Label hint = new()
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "Ask the employee before using Employee Credit. Employee Credit is never selected automatically. If the employee declines, leave Employee Credit at $0.00 and collect enough cash or modify the sale. The sale can complete only when the order is fully covered.",
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 8, 0, 8)
        };
        table.Controls.Add(hint, 0, 7);
        table.SetColumnSpan(hint, 2);

        FlowLayoutPanel buttons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        Button cancel = new() { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(this.complete);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 8);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
        CancelButton = cancel;
        UpdatePaymentState();
        Shown += (_, _) => this.cashReceived.Focus();
    }

    private void UpdatePaymentState()
    {
        bool valid = TryCurrentEditorValue(this.account, out _) && TryCurrentEditorValue(this.cashReceived, out _);
        if (!valid)
        {
            this.remaining.Text = "—";
            this.change.Text = "—";
            this.newResponsibility.Text = "—";
            this.complete.Enabled = false;
            return;
        }

        this.remaining.Text = RemainingAmount.ToString("C2");
        this.change.Text = ChangeAmount.ToString("C2");
        decimal addedResponsibility = Math.Max(0m, CafeteriaAccountAmount - this.subsidyRemaining);
        this.newResponsibility.Text = addedResponsibility.ToString("C2");
        this.newResponsibility.ForeColor = addedResponsibility > 0m ? Color.DarkOrange : Color.DarkGreen;
        this.remaining.ForeColor = RemainingAmount > 0m ? Color.Firebrick : Color.DarkGreen;
        this.change.ForeColor = ChangeAmount > 0m ? Color.DarkGreen : SystemColors.ControlText;
        this.complete.Enabled = RemainingAmount == 0m;
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

        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out value))
        {
            return false;
        }

        if (value < control.Minimum || value > control.Maximum)
        {
            return false;
        }

        return true;
    }

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 0, 7)
    };

    private static Label ValueLabel(string text = "") => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 12, FontStyle.Bold),
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 7, 0, 7)
    };
}
