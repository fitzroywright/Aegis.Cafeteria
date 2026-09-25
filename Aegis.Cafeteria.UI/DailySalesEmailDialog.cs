namespace Aegis.Cafeteria.UI;

public sealed class DailySalesEmailDialog : Form
{
    private readonly TextBox recipients = new()
    {
          Multiline = true
        , ScrollBars = ScrollBars.Vertical
        , Width = 500
        , Height = 110
    };

    public IReadOnlyList<string> Recipients { get; private set; } = [];

    public DailySalesEmailDialog(string? defaultRecipient)
    {
        Text = "Email Daily Sales Report";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(580, 300);
        Font = new Font("Segoe UI", 10);

        if (!string.IsNullOrWhiteSpace(defaultRecipient))
        {
            this.recipients.Text = defaultRecipient.Trim();
        }

        TableLayoutPanel root = new()
        {
              Dock = DockStyle.Fill
            , Padding = new Padding(24)
            , RowCount = 6
            , ColumnCount = 1
        };

        root.Controls.Add(new Label
        {
              Text = "Email Daily Sales Report"
            , AutoSize = true
            , Font = new Font("Segoe UI", 16, FontStyle.Bold)
        });

        root.Controls.Add(new Label
        {
              Text = "Enter one or more email addresses. Separate addresses with commas, semicolons, or new lines."
            , AutoSize = true
            , MaximumSize = new Size(510, 0)
            , ForeColor = Color.DimGray
            , Margin = new Padding(0, 6, 0, 10)
        });

        root.Controls.Add(this.recipients);

        FlowLayoutPanel actions = new()
        {
              AutoSize = true
            , FlowDirection = FlowDirection.RightToLeft
            , Dock = DockStyle.Top
            , Margin = new Padding(0, 14, 0, 0)
        };

        Button cancel = new() { Text = "Cancel", Width = 100, DialogResult = DialogResult.Cancel };
        Button send = new() { Text = "Email Report", Width = 120 };
        send.Click += (_, _) => Confirm();

        actions.Controls.Add(cancel);
        actions.Controls.Add(send);
        root.Controls.Add(actions);

        Controls.Add(root);
        AcceptButton = send;
        CancelButton = cancel;
    }

    private void Confirm()
    {
        string[] values = this.recipients.Text
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (values.Length == 0)
        {
            MessageBox.Show(this, "Enter at least one email address.", "Email Daily Sales Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        this.Recipients = values;
        DialogResult = DialogResult.OK;
        Close();
    }
}
