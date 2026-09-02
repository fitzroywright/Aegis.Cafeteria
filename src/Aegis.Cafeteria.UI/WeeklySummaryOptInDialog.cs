namespace Aegis.Cafeteria.UI;

using System.Net.Mail;

public sealed class WeeklySummaryOptInDialog : Form
{
    private readonly TextBox emailAddress = new() { Width = 330 };

    public WeeklySummaryOptInDialog(string customerName, string? suggestedEmailAddress = null)
    {
        Text = "Weekly Cafeteria Summary";
        Width = 480;
        Height = 245;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        Label prompt = new()
        {
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Text = $"{customerName}, would you like a weekly email summary of your cafeteria transactions?"
        };
        Label emailLabel = new() { AutoSize = true, Text = "Email address" };
        this.emailAddress.Text = suggestedEmailAddress ?? string.Empty;

        Button yes = new() { Text = "Yes", Width = 90 };
        Button no = new() { Text = "No", Width = 90, DialogResult = DialogResult.No };
        yes.Click += (_, _) =>
        {
            try
            {
                MailAddress address = new(this.emailAddress.Text.Trim());
                this.emailAddress.Text = address.Address;
                DialogResult = DialogResult.Yes;
                Close();
            }
            catch (FormatException)
            {
                MessageBox.Show(this, "Enter a valid email address.", "Weekly Summary", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.emailAddress.Focus();
            }
        };

        FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(yes);
        buttons.Controls.Add(no);

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.Controls.Add(prompt, 0, 0);
        layout.Controls.Add(emailLabel, 0, 1);
        layout.Controls.Add(this.emailAddress, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = yes;
        CancelButton = no;
    }

    public string EmailAddress => this.emailAddress.Text.Trim();
}
