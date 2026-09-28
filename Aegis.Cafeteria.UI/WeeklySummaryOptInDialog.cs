namespace Aegis.Cafeteria.UI;

public sealed class WeeklySummaryOptInDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);

    private readonly TextBox emailAddress = new()
    {
        Dock = DockStyle.Fill,
        Height = 36,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10.5f)
    };

    public WeeklySummaryOptInDialog(string customerName, string? suggestedEmailAddress = null, bool needsReview = false)
    {
        Text = needsReview ? "Confirm email address" : "Weekly Cafeteria Summary";
        Width = 680;
        Height = 410;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10);

        Label prompt = new()
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Text = needsReview
                ? $"{customerName}'s work email details have changed. Confirm the address for cafeteria emails."
                : $"Would {customerName} like a weekly email summary of cafeteria transactions?",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = needsReview ? Color.DarkGoldenrod : Navy,
            Margin = new Padding(0, 0, 0, 8)
        };

        Label help = new()
        {
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Text = needsReview
                ? "Keep the address below, edit it to use another address, stop emails, or ask again after a later sale."
                : "Confirm the email address below. Choosing No records the preference and prevents future automatic prompts.",
            ForeColor = Muted,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, 0, 0, 18)
        };

        Label emailLabel = new()
        {
            AutoSize = true,
            Text = "EMAIL ADDRESS",
            ForeColor = Navy,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };
        this.emailAddress.Text = suggestedEmailAddress ?? string.Empty;

        Button yes = ActionButton(needsReview ? "Keep / save address" : "Yes, send summaries", Green, 175);
        Button no = SecondaryButton(needsReview ? "Stop emails" : "No, don't send", 145);
        Button later = SecondaryButton("Ask later", 110);
        later.DialogResult = DialogResult.Cancel;
        no.DialogResult = DialogResult.No;

        yes.Click += (_, _) =>
        {
            if (!EmailAddressValidator.TryNormalize(this.emailAddress.Text, out string normalizedAddress))
            {
                MessageBox.Show
                (
                    this,
                    "Enter a valid email address before enabling weekly summaries.",
                    "Weekly Cafeteria Summary",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                this.emailAddress.Focus();
                this.emailAddress.SelectAll();
                return;
            }

            this.emailAddress.Text = normalizedAddress;
            DialogResult = DialogResult.Yes;
            Close();
        };

        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Navy,
            Padding = new Padding(22, 14, 22, 12)
        };
        FlowLayoutPanel headerStack = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        headerStack.Controls.Add(new Label
        {
            Text = "CAFETERIA EMAIL PREFERENCES",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        });
        headerStack.Controls.Add(new Label
        {
            Text = "Weekly transaction summary",
            AutoSize = true,
            ForeColor = Color.FromArgb(193, 210, 225),
            Font = new Font("Segoe UI", 9)
        });
        header.Controls.Add(headerStack);

        Panel card = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(24)
        };

        TableLayoutPanel content = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            AutoSize = true
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(prompt, 0, 0);
        content.Controls.Add(help, 0, 1);
        content.Controls.Add(emailLabel, 0, 2);
        content.Controls.Add(this.emailAddress, 0, 3);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 18, 0, 0)
        };
        buttons.Controls.Add(later);
        buttons.Controls.Add(no);
        buttons.Controls.Add(yes);
        content.Controls.Add(buttons, 0, 5);
        card.Controls.Add(content);

        Controls.Add(card);
        Controls.Add(header);
        AcceptButton = yes;
        CancelButton = later;
    }

    public string EmailAddress => this.emailAddress.Text.Trim();

    private static Button ActionButton(string text, Color backColor, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 42,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Margin = new Padding(8, 0, 0, 0)
    };

    private static Button SecondaryButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 42,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = Navy,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Margin = new Padding(8, 0, 0, 0)
    };
}

