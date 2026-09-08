namespace Aegis.Cafeteria.UI;

public sealed class ReasonDialog : Form
{
    private readonly TextBox reason = new() { Multiline = true, Width = 340, Height = 90, MaxLength = 500 };
    private readonly Button ok = new() { Text = "Continue", Width = 100, Height = 34 };
    public string Reason => reason.Text.Trim();

    public ReasonDialog(string title, string prompt, string? defaultReason = null)
    {
        Text = title;
        this.reason.Text = defaultReason?.Trim() ?? string.Empty;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(410, 220);
        Font = new Font("Segoe UI", 10);
        AcceptButton = ok;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), RowCount = 4, ColumnCount = 1 };
        root.Controls.Add(new Label { Text = prompt, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        root.Controls.Add(reason);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        var cancel = new Button { Text = "Cancel", Width = 90, Height = 34, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons);
        Controls.Add(root);

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(reason.Text))
            {
                MessageBox.Show(this, "A reason is required for this action.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        Shown += (_, _) =>
        {
            this.reason.Focus();
            this.reason.SelectAll();
        };
    }
}
