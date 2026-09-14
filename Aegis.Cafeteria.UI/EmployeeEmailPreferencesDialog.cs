namespace Aegis.Cafeteria.UI;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Services;

public sealed class EmployeeEmailPreferencesDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Yellow = Color.FromArgb(255, 248, 220);
    private static readonly Color YellowBorder = Color.FromArgb(235, 185, 70);

    private readonly ICustomerIdentificationService identification;
    private readonly CafeteriaServicesClient servicesClient;
    private readonly TextBox search = new()
    {
        Dock = DockStyle.Fill,
        Height = 36,
        PlaceholderText = "Name, code, TRN, or identifier",
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10.5f)
    };
    private readonly Button find = ActionButton("Find", Blue, 88);
    private readonly ListBox results = new()
    {
        Dock = DockStyle.Fill,
        Height = 132,
        DisplayMember = nameof(EmployeeChoice.DisplayText),
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10)
    };
    private readonly CheckBox enabled = new()
    {
        AutoSize = true,
        Text = "Send weekly cafeteria summary",
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        ForeColor = Navy,
        BackColor = Yellow,
        Margin = new Padding(0, 0, 0, 10)
    };
    private readonly TextBox email = new()
    {
        Dock = DockStyle.Fill,
        Height = 36,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10.5f),
        BackColor = Yellow
    };
    private readonly Label status = new()
    {
        AutoSize = true,
        MaximumSize = new Size(620, 0),
        ForeColor = Muted,
        Font = new Font("Segoe UI", 9.5f),
        Margin = new Padding(0, 10, 0, 0)
    };
    private readonly Button save = ActionButton("Save", Green, 96);
    private int selectedCustomerId;

    public EmployeeEmailPreferencesDialog(
        ICustomerIdentificationService identification,
        CafeteriaServicesClient servicesClient)
    {
        this.identification = identification;
        this.servicesClient = servicesClient;

        Text = "Employee Email Preferences";
        ClientSize = new Size(720, 565);
        MinimumSize = new Size(736, 604);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10);
        Padding = Padding.Empty;

        this.save.Enabled = false;
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

        Button close = SecondaryButton("Close", 96);
        close.DialogResult = DialogResult.Cancel;
        AcceptButton = this.find;
        CancelButton = close;

        Controls.Add(BuildBody(close));
        Controls.Add(BuildHeader());
    }

    private Control BuildHeader()
    {
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
            Text = "EMPLOYEE EMAIL PREFERENCES",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        });
        headerStack.Controls.Add(new Label
        {
            Text = "Weekly cafeteria transaction summaries",
            AutoSize = true,
            ForeColor = Color.FromArgb(193, 210, 225),
            Font = new Font("Segoe UI", 9)
        });
        header.Controls.Add(headerStack);
        return header;
    }

    private Control BuildBody(Button close)
    {
        Panel card = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(28, 24, 28, 20)
        };

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 148F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label findEmployee = new()
        {
            Text = "FIND EMPLOYEE",
            AutoSize = true,
            ForeColor = Blue,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };
        layout.Controls.Add(findEmployee, 0, 0);

        TableLayoutPanel searchRow = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 12)
        };
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.find.Margin = new Padding(8, 0, 0, 0);
        searchRow.Controls.Add(this.search, 0, 0);
        searchRow.Controls.Add(this.find, 1, 0);
        layout.Controls.Add(searchRow, 0, 1);

        this.results.Margin = new Padding(0, 0, 0, 16);
        layout.Controls.Add(this.results, 0, 2);

        Panel preferenceCard = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            BackColor = Yellow,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };
        preferenceCard.Paint += (_, eventArgs) =>
        {
            using Pen border = new(YellowBorder);
            Rectangle rectangle = preferenceCard.ClientRectangle;
            rectangle.Width -= 1;
            rectangle.Height -= 1;
            eventArgs.Graphics.DrawRectangle(border, rectangle);
        };

        TableLayoutPanel preferenceLayout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Yellow
        };
        preferenceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        preferenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preferenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preferenceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preferenceLayout.Controls.Add(this.enabled, 0, 0);
        preferenceLayout.Controls.Add(new Label
        {
            Text = "EMAIL ADDRESS",
            AutoSize = true,
            ForeColor = Navy,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            BackColor = Yellow,
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 1);
        preferenceLayout.Controls.Add(this.email, 0, 2);
        preferenceCard.Controls.Add(preferenceLayout);
        layout.Controls.Add(preferenceCard, 0, 3);

        Panel statusHost = new()
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        statusHost.Controls.Add(this.status);
        layout.Controls.Add(statusHost, 0, 4);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0),
            Padding = Padding.Empty
        };
        close.Margin = new Padding(8, 0, 0, 0);
        this.save.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(close);
        buttons.Controls.Add(this.save);
        layout.Controls.Add(buttons, 0, 5);

        card.Controls.Add(layout);
        return card;
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
        this.email.BackColor = this.enabled.Checked ? Yellow : Color.FromArgb(239, 241, 243);
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
            if (!EmailAddressValidator.TryNormalize(this.email.Text, out string normalizedAddress))
            {
                MessageBox.Show
                (
                    this,
                    "Enter a valid email address before enabling weekly summaries.",
                    "Employee Email Preferences",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                this.email.Focus();
                this.email.SelectAll();
                return;
            }

            emailAddress = normalizedAddress;
            this.email.Text = normalizedAddress;
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

    private static Button ActionButton(string text, Color backColor, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 42,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Cursor = Cursors.Hand
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
        Cursor = Cursors.Hand
    };

    private sealed record EmployeeChoice(CustomerSearchCandidate Customer)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(this.Customer.Department)
            ? this.Customer.Name
            : $"{this.Customer.Name} — {this.Customer.Department}";
    }
}
