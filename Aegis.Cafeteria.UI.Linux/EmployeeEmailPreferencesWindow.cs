namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Services;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Net.Mail;

public sealed class EmployeeEmailPreferencesWindow : Window
{
    private readonly ICustomerIdentificationService identification;
    private readonly CafeteriaServicesClient servicesClient;

    private readonly TextBox search = new()
    {
          Watermark = "Name, code, TRN, or identifier"
        , MinWidth = 420
    };

    private readonly Button find = new()
    {
          Content = "Find"
        , MinWidth = 90
    };

    private readonly ListBox results = new()
    {
          MinHeight = 150
        , MaxHeight = 180
    };

    private readonly CheckBox enabled = new()
    {
        Content = "Send weekly cafeteria summary"
    };

    private readonly TextBox email = new()
    {
        Watermark = "Email address"
    };

    private readonly TextBlock status = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };

    private readonly Button save = new()
    {
          Content = "Save"
        , MinWidth = 100
        , IsEnabled = false
    };

    private CustomerNotificationPreferenceSnapshot? loadedPreference;
    private int selectedCustomerId;
    private CustomerSearchCandidate? selectedCustomer;

    public EmployeeEmailPreferencesWindow
    (
          ICustomerIdentificationService identification
        , CafeteriaServicesClient servicesClient
    )
    {
        this.identification = identification;
        this.servicesClient = servicesClient;

        Title = "Employee Email Preferences";
        Width = 720;
        Height = 590;
        MinWidth = 660;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.find.Click += async (_, _) => await SearchAsync();
        this.search.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key != Avalonia.Input.Key.Enter)
            {
                return;
            }

            eventArgs.Handled = true;
            await SearchAsync();
        };

        this.results.SelectionChanged += async (_, _) => await LoadSelectedAsync();
        this.enabled.IsCheckedChanged += (_, _) => UpdateEditorState();
        this.save.Click += async (_, _) => await SaveAsync();

        Content = BuildContent();
        UpdateEditorState();
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
              Margin = new Avalonia.Thickness(24)
            , RowSpacing = 12
        };

        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(170)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock heading = new()
        {
              Text = "EMPLOYEE EMAIL PREFERENCES"
            , FontSize = 18
            , FontWeight = Avalonia.Media.FontWeight.Bold
        };
        root.Children.Add(heading);

        Grid searchRow = new()
        {
            ColumnSpacing = 8
        };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        searchRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        searchRow.Children.Add(this.search);
        Grid.SetColumn(this.find, 1);
        searchRow.Children.Add(this.find);
        Grid.SetRow(searchRow, 1);
        root.Children.Add(searchRow);

        Grid.SetRow(this.results, 2);
        root.Children.Add(this.results);

        StackPanel preference = new()
        {
              Spacing = 8
            , Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };
        preference.Children.Add(this.enabled);
        preference.Children.Add(new TextBlock
        {
              Text = "Email address"
            , FontWeight = Avalonia.Media.FontWeight.Bold
        });
        preference.Children.Add(this.email);
        Grid.SetRow(preference, 3);
        root.Children.Add(preference);

        Grid.SetRow(this.status, 4);
        root.Children.Add(this.status);

        StackPanel buttons = new()
        {
              Orientation = Orientation.Horizontal
            , HorizontalAlignment = HorizontalAlignment.Right
            , Spacing = 10
        };
        Button close = new()
        {
              Content = "Close"
            , MinWidth = 100
        };
        close.Click += (_, _) => Close();
        buttons.Children.Add(this.save);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        return root;
    }

    private async Task SearchAsync()
    {
        string searchText = this.search.Text?.Trim() ?? string.Empty;
        if (searchText.Length == 0)
        {
            this.status.Text = "Enter a name, code, TRN, or identifier.";
            return;
        }

        this.find.IsEnabled = false;
        this.save.IsEnabled = false;
        this.status.Text = "Searching local employee cache...";

        try
        {
            IReadOnlyList<CustomerSearchCandidate> candidates = await this.identification.SearchAsync(searchText, 30);
            EmployeeChoice[] choices = candidates
                .Where(candidate => candidate.IsEligibleForCredit)
                .Select(candidate => new EmployeeChoice(candidate))
                .ToArray();

            this.results.ItemsSource = choices;
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
            this.find.IsEnabled = true;
        }
    }

    private async Task LoadSelectedAsync()
    {
        if (this.results.SelectedItem is not EmployeeChoice choice)
        {
            this.selectedCustomerId = 0;
            this.selectedCustomer = null;
            this.save.IsEnabled = false;
            return;
        }

        this.loadedPreference = null;
        this.selectedCustomerId = choice.Customer.CustomerId;
        int loadingCustomerId = this.selectedCustomerId;
        this.selectedCustomer = choice.Customer;
        this.save.IsEnabled = false;
        this.status.Text = "Loading saved preference...";

        try
        {
            CustomerNotificationPreferenceSnapshot preference =
                await this.servicesClient.GetNotificationPreferenceAsync(loadingCustomerId);
            if (this.selectedCustomerId != loadingCustomerId)
            {
                return;
            }

            this.loadedPreference = preference;
            this.status.Foreground = preference.NeedsEmailReview ? Avalonia.Media.Brushes.DarkGoldenrod : Avalonia.Media.Brushes.Black;

            this.enabled.IsChecked = preference.WeeklySummaryEnabled;
            this.email.Text = !string.IsNullOrWhiteSpace(preference.EmailAddress)
                ? preference.EmailAddress
                : preference.SuggestedEmailSource is not null ? preference.SuggestedEmailAddress : choice.Customer.EmailAddress;

            this.status.Text = preference.NeedsEmailReview
                ? "CONFIRM EMAIL ADDRESS: Work email details changed. Ask the employee to keep or change this address, or disable emails. Save records their choice."
                : preference.PreferenceAsked
                ? "This employee has already made an email-summary choice. You can change it here."
                : "No preference has been recorded yet. The cached employee email is shown when available.";
        }
        catch (Exception exception)
        {
            if (this.selectedCustomerId != loadingCustomerId)
            {
                return;
            }

            this.enabled.IsChecked = false;
            this.email.Text = choice.Customer.EmailAddress;
            this.status.Text = $"Could not load saved preference: {exception.Message}";
        }

        UpdateEditorState();
        this.save.IsEnabled = this.loadedPreference is not null;
    }

    private void UpdateEditorState()
    {
        this.email.IsEnabled = this.enabled.IsChecked == true;
    }

    private async Task SaveAsync()
    {
        if (this.selectedCustomerId <= 0 || this.selectedCustomer is null || this.loadedPreference is null)
        {
            this.status.Text = "Select an employee first.";
            return;
        }

        int savingCustomerId = this.selectedCustomerId;
        string? emailAddress = null;
        if (this.enabled.IsChecked == true)
        {
            try
            {
                MailAddress address = new(this.email.Text?.Trim() ?? string.Empty);
                emailAddress = address.Address;
                this.email.Text = emailAddress;
            }
            catch (FormatException)
            {
                this.status.Text = "Enter a valid email address before enabling weekly summaries.";
                this.email.Focus();
                this.email.SelectAll();
                return;
            }
        }

        this.save.IsEnabled = false;

        try
        {
            CustomerNotificationPreferenceSnapshot saved =
                await this.servicesClient.SetNotificationPreferenceAsync
                (
                      this.selectedCustomerId
                    , this.enabled.IsChecked == true
                    , emailAddress
                    , suggestedEmailSource: NotificationEmailChoice.AcknowledgedSource(this.loadedPreference, this.enabled.IsChecked == true, emailAddress)
                    , suggestedEmailAddress: this.loadedPreference.SuggestedEmailAddress
                );
            if (this.selectedCustomerId != savingCustomerId)
            {
                return;
            }

            this.loadedPreference = saved;

            this.status.Text = saved.NeedsEmailReview
                ? "The work email changed again while saving. Confirm the email address after the next sale."
                : saved.WeeklySummaryEnabled
                ? $"Weekly summaries enabled for {saved.EmailAddress}."
                : "Weekly summaries disabled. The employee will not be prompted again automatically.";
        }
        catch (Exception exception)
        {
            if (this.selectedCustomerId == savingCustomerId)
            {
                this.status.Text = $"Could not save preference: {exception.Message}";
            }
        }
        finally
        {
            this.save.IsEnabled = this.loadedPreference?.CustomerId == this.selectedCustomerId;
        }
    }

    private sealed record EmployeeChoice(CustomerSearchCandidate Customer)
    {
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(this.Customer.Department)
                ? this.Customer.Name
                : $"{this.Customer.Name} — {this.Customer.Department}";
        }
    }
}

