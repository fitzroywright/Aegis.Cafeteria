namespace Aegis.Cafeteria.UI;

internal sealed class TenderDialog : Form
{
    private readonly CheckBox employeeCredit = new()
    {
        Text = "Use Employee Credit",
        AutoSize = true,
        Checked = false,
        Dock = DockStyle.Left
    };

    private readonly NumericUpDown account = new()
    {
        DecimalPlaces = 2,
        Maximum = 100000000,
        Minimum = 0,
        ThousandsSeparator = true,
        Dock = DockStyle.Fill,
        Enabled = false
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

    public bool EmployeeCreditSelected => this.employeeCredit.Checked;
    public decimal CafeteriaAccountAmount => EmployeeCreditSelected ? CurrentEditorValue(this.account) : 0m;
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
        Size = new Size(500, 500);

        this.maximumAccountAmount = Math.Min(total, Math.Max(0m, availableCredit));
        this.account.Maximum = this.maximumAccountAmount;
        this.account.Value = 0m;
        this.account.Enabled = false;
        this.employeeCredit.Enabled = this.maximumAccountAmount > 0m;
        this.cashReceived.Value = 0m;

        // Invariant: application code never turns Employee Credit on.
        // Checked may become true only through the user's interaction with this checkbox.
        this.employeeCredit.CheckedChanged += (_, _) => OnEmployeeCreditSelectionChanged();
        this.account.ValueChanged += (_, _) => UpdatePaymentState();
        this.cashReceived.ValueChanged += (_, _) => UpdatePaymentState();
        this.account.TextChanged += (_, _) => UpdatePaymentState();
        this.cashReceived.TextChanged += (_, _) => UpdatePaymentState();
        this.complete.Click += (_, _) =>
        {
            decimal accountAmount = CafeteriaAccountAmount;
            if (!TryCurrentEditorValue(this.cashReceived, out decimal cashReceived) ||
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
            RowCount = 10,
            Padding = new Padding(18)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        table.Controls.Add(Caption("Order Total"), 0, 0);
        table.Controls.Add(ValueLabel(total.ToString("C2")), 1, 0);

        table.Controls.Add(Caption("Available Employee Credit"), 0, 1);
        table.Controls.Add(ValueLabel(availableCredit.ToString("C2")), 1, 1);

        table.Controls.Add(Caption("Employee Credit"), 0, 2);
        table.Controls.Add(this.employeeCredit, 1, 2);

        table.Controls.Add(Caption("Employee Credit Amount"), 0, 3);
        table.Controls.Add(this.account, 1, 3);

        table.Controls.Add(Caption("New Customer Responsibility"), 0, 4);
        table.Controls.Add(this.newResponsibility, 1, 4);

        table.Controls.Add(Caption("Cash Received"), 0, 5);
        table.Controls.Add(this.cashReceived, 1, 5);

        table.Controls.Add(Caption("Remaining"), 0, 6);
        table.Controls.Add(this.remaining, 1, 6);

        table.Controls.Add(Caption("Change Due"), 0, 7);
        table.Controls.Add(this.change, 1, 7);

        Label hint = new()
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "Employee Credit always starts off. Only the cashier may select Use Employee Credit after confirming with the employee. The system never selects it automatically.",
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 8, 0, 8)
        };
        table.Controls.Add(hint, 0, 8);
        table.SetColumnSpan(hint, 2);

        FlowLayoutPanel buttons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill
        };
        Button cancel = new() { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(this.complete);
        buttons.Controls.Add(cancel);
        table.Controls.Add(buttons, 0, 9);
        table.SetColumnSpan(buttons, 2);

        Controls.Add(table);
        CancelButton = cancel;
        UpdatePaymentState();
        Shown += (_, _) => this.cashReceived.Focus();
    }

    private void OnEmployeeCreditSelectionChanged()
    {
        this.account.Enabled = this.employeeCredit.Checked && this.maximumAccountAmount > 0m;

        if (!this.employeeCredit.Checked)
        {
            this.account.Value = 0m;
        }

        UpdatePaymentState();
    }

    private void UpdatePaymentState()
    {
        bool accountValid = !EmployeeCreditSelected || TryCurrentEditorValue(this.account, out _);
        bool cashValid = TryCurrentEditorValue(this.cashReceived, out _);
        if (!accountValid || !cashValid)
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
