namespace Aegis.Cafeteria.UI;

public sealed class CustomerPaymentDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private readonly NumericUpDown amount = new() { DecimalPlaces = 2, ThousandsSeparator = true, Minimum = 0.01m, Maximum = 1_000_000m, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly ComboBox method = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox reference = new() { Dock = DockStyle.Fill, PlaceholderText = "Optional receipt / POS / bank reference" };
    private readonly Label remaining = new() { AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = Navy };
    private readonly decimal amountDue;

    public CustomerPaymentDialog(string customerName, decimal amountDue)
    {
        this.amountDue = amountDue;
        Text = "Customer Payment";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 590;
        Height = 455;
        Padding = new Padding(20);
        BackColor = Color.White;

        this.amount.Maximum = Math.Max(0.01m, amountDue);
        this.amount.Value = Math.Min(this.amount.Maximum, amountDue);
        this.method.Items.AddRange(["Cash", "Card / POS", "Bank Transfer", "Cheque", "Other"]);
        this.method.SelectedIndex = 0;
        this.amount.ValueChanged += (_, _) => UpdateRemaining();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
        for (var i = 0; i < 8; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label { Text = "CUSTOMER PAYMENT", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Navy, Margin = new Padding(0, 0, 0, 2) };
        var subtitle = new Label { Text = "Record money received from the employee. This reduces the Employee Amount Due for the current period.", AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 14) };
        root.Controls.Add(title, 0, 0); root.SetColumnSpan(title, 2);
        root.Controls.Add(subtitle, 0, 1); root.SetColumnSpan(subtitle, 2);

        AddRow(root, 2, "Customer", new Label { Text = customerName, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        AddRow(root, 3, "Employee Amount Due before payment", new Label { Text = amountDue.ToString("C2"), AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = Navy });
        AddRow(root, 4, "Payment amount", this.amount);
        AddRow(root, 5, "Payment method", this.method);
        AddRow(root, 6, "Reference", this.reference);
        AddRow(root, 7, "Employee Amount Due after payment", this.remaining);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 16, 0, 0) };
        var apply = new Button { Text = "Apply Payment", AutoSize = true, Height = 40, DialogResult = DialogResult.OK, BackColor = Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        apply.FlatAppearance.BorderSize = 0;
        var cancel = new Button { Text = "Cancel", AutoSize = true, Height = 40, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(apply);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 8); root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = apply;
        CancelButton = cancel;
        UpdateRemaining();
    }

    public decimal Amount => this.amount.Value;
    public string PaymentMethod => this.method.SelectedItem?.ToString() ?? "Cash";
    public string? Reference => string.IsNullOrWhiteSpace(this.reference.Text) ? null : this.reference.Text.Trim();

    private void UpdateRemaining() => this.remaining.Text = Math.Max(0m, this.amountDue - this.amount.Value).ToString("C2");

    private static void AddRow(TableLayoutPanel root, int row, string caption, Control value)
    {
        root.Controls.Add(new Label { Text = caption, AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Muted, Margin = new Padding(0, 9, 8, 9) }, 0, row);
        value.Margin = new Padding(0, 6, 0, 6);
        root.Controls.Add(value, 1, row);
    }
}
