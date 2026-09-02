namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Services;

using System.Net.Mail;

public sealed class EmployeeEmailPreferencesDialog : Form
{
    private readonly ICustomerIdentificationService identification;
    private readonly CafeteriaServicesClient servicesClient;
    private readonly TextBox search = new() { Width = 330, PlaceholderText = "Name, code, TRN, or identifier" };
    private readonly Button find = new() { Text = "Find", Width = 85 };
    private readonly ListBox results = new() { Width = 430, Height = 130, DisplayMember = nameof(EmployeeChoice.DisplayText) };
    private readonly CheckBox enabled = new() { AutoSize = true, Text = "Send weekly cafeteria summary" };
    private readonly TextBox email = new() { Width = 330 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(430, 0) };
    private readonly Button save = new() { Text = "Save", Width = 90, Enabled = false };
    private int selectedCustomerId;

    public EmployeeEmailPreferencesDialog(
        ICustomerIdentificationService identification,
        CafeteriaServicesClient servicesClient)
    {
        this.identification = identification;
        this.servicesClient = servicesClient;

        Text = "Employee Email Preferences";
        Width = 520;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        this.find.Click += async (_, _) => await SearchAsync();
        this.search.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter)
            {
                return;
            }

            eventArgs.SuppressKeyPress = true;
            await SearchAsync();
        };
        this.results.SelectedIndexChanged += async (_, _) => await LoadSelectedAsync();
        this.enabled.CheckedChanged += (_, _) => UpdateEditorState();
        this.save.Click += async (_, _) => await SaveAsync();

        Button close = new() { Text = "Close", Width = 90, DialogResult = DialogResult.Cancel };
        AcceptButton = this.find;
        CancelButton = close;

        FlowLayoutPanel searchRow = new() { AutoSize = true, WrapContents = false };
        searchRow.Controls.Add(this.search);
        searchRow.Controls.Add(this.find);

        FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = false };
        buttons.Controls.Add(this.save);
        buttons.Controls.Add(close);

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 9,
            AutoSize = true
        };
        layout.Controls.Add(new Label { Text = "Find employee", AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(searchRow, 0, 1);
        layout.Controls.Add(this.results, 0, 2);
        layout.Controls.Add(this.enabled, 0, 3);
        layout.Controls.Add(new Label { Text = "Email address", AutoSize = true }, 0, 4);
        layout.Controls.Add(this.email, 0, 5);
        layout.Controls.Add(this.status, 0, 6);
        layout.Controls.Add(buttons, 0, 7);
        Controls.Add(layout);
    }

    private async Task SearchAsync()
    {
        string searchText = this.search.Text.Trim();
        if (searchText.Length == 0)
        {
            this.status.Text = "Enter a name, code, TRN, or identifier.";
            return;
        }

        this.find.Enabled = false;
        this.status.Text = "Searching local employee cache...";
        try
        {
            IReadOnlyList<CustomerSearchCandidate> candidates = await this.identification.SearchAsync(searchText, 30);
            EmployeeChoice[] choices = candidates
                .Where(candidate => candidate.IsEligibleForCredit)
                .Select(candidate => new EmployeeChoice(candidate))
                .ToArray();
            this.results.DataSource = choices;
            this.status.Text = choices.Length == 0
                ? "No eligible cafeteria employees matched your search."
                : $"{choices.Length} employee(s) found.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Employee search failed: {exception.Message}";
        }
        finally
        {
            this.find.Enabled = true;
        }
    }

    private async Task LoadSelectedAsync()
    {
        if (this.results.SelectedItem is not EmployeeChoice choice)
        {
            this.selectedCustomerId = 0;
            this.save.Enabled = false;
            return;
        }

        this.selectedCustomerId = choice.Customer.CustomerId;
        this.save.Enabled = false;
        this.status.Text = "Loading saved preference...";

        try
        {
            CustomerNotificationPreferenceSnapshot preference = await this.servicesClient.GetNotificationPreferenceAsync(this.selectedCustomerId);
            this.enabled.Checked = preference.WeeklySummaryEnabled;
            this.email.Text = !string.IsNullOrWhiteSpace(preference.EmailAddress)
                ? preference.EmailAddress
                : choice.Customer.EmailAddress;
            this.status.Text = preference.PreferenceAsked
                ? "This employee has already made an email-summary choice. You can change it here."
                : "No preference has been recorded yet. The cached employee email is shown when available.";
            UpdateEditorState();
            this.save.Enabled = true;
        }
        catch (Exception exception)
        {
            this.enabled.Checked = false;
            this.email.Text = choice.Customer.EmailAddress;
            this.status.Text = $"Could not load saved preference: {exception.Message}";
            UpdateEditorState();
            this.save.Enabled = true;
        }
    }

    private void UpdateEditorState()
    {
        this.email.Enabled = this.enabled.Checked;
    }

    private async Task SaveAsync()
    {
        if (this.selectedCustomerId <= 0)
        {
            this.status.Text = "Select an employee first.";
            return;
        }

        string? emailAddress = null;
        if (this.enabled.Checked)
        {
            try
            {
                MailAddress address = new(this.email.Text.Trim());
                emailAddress = address.Address;
                this.email.Text = emailAddress;
            }
            catch (FormatException)
            {
                MessageBox.Show(this, "Enter a valid email address.", "Employee Email Preferences", MessageBoxButtons.OK, MessageBoxIcon.Information);
                this.email.Focus();
                return;
            }
        }

        this.save.Enabled = false;
        try
        {
            CustomerNotificationPreferenceSnapshot saved = await this.servicesClient.SetNotificationPreferenceAsync(
                this.selectedCustomerId,
                this.enabled.Checked,
                emailAddress);
            this.status.Text = saved.WeeklySummaryEnabled
                ? $"Weekly summaries enabled for {saved.EmailAddress}."
                : "Weekly summaries disabled. The employee will not be prompted again automatically.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not save preference: {exception.Message}";
        }
        finally
        {
            this.save.Enabled = true;
        }
    }

    private sealed record EmployeeChoice(CustomerSearchCandidate Customer)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(this.Customer.Department)
            ? this.Customer.Name
            : $"{this.Customer.Name} — {this.Customer.Department}";
    }
}
