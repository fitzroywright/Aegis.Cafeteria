namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Infrastructure.Services;
using Avalonia.Controls;
using Avalonia.Layout;
using System.Collections.ObjectModel;

internal sealed class OperationalAuditWindow : Window
{
    private readonly CafeteriaManagementClient client;
    private readonly TextBox actor = new() { Width = 150 };
    private readonly TextBox action = new() { Width = 170 };
    private readonly TextBox customerId = new() { Width = 90 };
    private readonly ComboBox result = new() { Width = 120 };
    private readonly TextBox take = new() { Width = 70, Text = "100" };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DataGrid grid = new() { IsReadOnly = true, AutoGenerateColumns = true };
    private readonly Button load = new() { Content = "Load Audit", MinWidth = 105 };
    private readonly Button integrity = new() { Content = "Verify Integrity", MinWidth = 115 };

    public OperationalAuditWindow(CafeteriaManagementClient client)
    {
        this.client = client;

        Title = "Cafeteria Operational Audit";
        Width = 1200;
        Height = 720;
        MinWidth = 960;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        this.result.ItemsSource = new[] { "All", "Succeeded", "Failed" };
        this.result.SelectedIndex = 0;
        this.load.Click += async (_, _) => await LoadAsync();
        this.integrity.Click += async (_, _) => await VerifyIntegrityAsync();
        Opened += async (_, _) => await LoadAsync();

        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        Grid root = new() { Margin = new Avalonia.Thickness(20), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(new TextBlock
        {
              Text = "OPERATIONAL AUDIT"
            , FontSize = 21
            , FontWeight = Avalonia.Media.FontWeight.Bold
        });

        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        filters.Children.Add(new TextBlock { Text = "Actor", VerticalAlignment = VerticalAlignment.Center });
        filters.Children.Add(this.actor);
        filters.Children.Add(new TextBlock { Text = "Action", VerticalAlignment = VerticalAlignment.Center });
        filters.Children.Add(this.action);
        filters.Children.Add(new TextBlock { Text = "Customer", VerticalAlignment = VerticalAlignment.Center });
        filters.Children.Add(this.customerId);
        filters.Children.Add(new TextBlock { Text = "Result", VerticalAlignment = VerticalAlignment.Center });
        filters.Children.Add(this.result);
        filters.Children.Add(new TextBlock { Text = "Rows", VerticalAlignment = VerticalAlignment.Center });
        filters.Children.Add(this.take);
        filters.Children.Add(this.load);
        filters.Children.Add(this.integrity);
        Grid.SetRow(filters, 1);
        root.Children.Add(filters);

        Grid.SetRow(this.grid, 2);
        root.Children.Add(this.grid);

        StackPanel footer = new()
        {
              Orientation = Orientation.Horizontal
            , HorizontalAlignment = HorizontalAlignment.Right
            , Spacing = 10
        };
        footer.Children.Add(this.status);
        Button close = new() { Content = "Close", MinWidth = 90 };
        close.Click += (_, _) => Close();
        footer.Children.Add(close);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private async Task LoadAsync()
    {
        int? selectedCustomer = null;
        if (!string.IsNullOrWhiteSpace(this.customerId.Text))
        {
            if (!int.TryParse(this.customerId.Text, out int parsed) || parsed <= 0)
            {
                this.status.Text = "Customer ID must be a positive number.";
                return;
            }
            selectedCustomer = parsed;
        }

        if (!int.TryParse(this.take.Text, out int selectedTake) || selectedTake is < 1 or > 500)
        {
            this.status.Text = "Rows must be between 1 and 500.";
            return;
        }

        bool? succeeded = this.result.SelectedIndex switch
        {
            1 => true,
            2 => false,
            _ => null
        };

        ToggleBusy(true);
        try
        {
            IReadOnlyList<AuditTrailSnapshot> rows = await this.client.GetAuditTrailAsync(
                  this.actor.Text
                , this.action.Text
                , selectedCustomer
                , succeeded
                , selectedTake);

            this.grid.ItemsSource = new ObservableCollection<AuditTrailSnapshot>(rows);
            this.status.Text = $"{rows.Count:N0} audit record(s) loaded.";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not load audit: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task VerifyIntegrityAsync()
    {
        ToggleBusy(true);
        try
        {
            AuditIntegritySnapshot audit = await this.client.GetAuditIntegrityAsync();
            this.status.Text = audit.IsValid
                ? $"Audit chain valid. Verified {audit.VerifiedRows:N0} row(s); legacy rows: {audit.LegacyRows:N0}."
                : $"Audit integrity failed at row {audit.FirstInvalidAuditId}: {audit.Error}";
        }
        catch (Exception exception)
        {
            this.status.Text = $"Audit integrity check failed: {exception.Message}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private void ToggleBusy(bool busy)
    {
        this.load.IsEnabled = !busy;
        this.integrity.IsEnabled = !busy;
    }
}
