namespace Aegis.Cafeteria.UI.Linux;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Export;
using Aegis.Cafeteria.Reporting.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using IOPath = System.IO.Path;

public sealed class ReportCenterWindow : Window
{
    private readonly ICafeteriaReportService reports;
    private readonly ICustomerIdentificationService identification;
    private readonly IProductCatalogQuery products;
    private readonly IReadOnlyDictionary<string, IReportExporter> exporters;

    private readonly ComboBox report = new();
    private readonly DatePicker from = new();
    private readonly DatePicker to = new();
    private readonly TextBlock description = new()
    {
          Foreground = new SolidColorBrush(Color.Parse("#687584"))
        , TextWrapping = TextWrapping.Wrap
    };

    private readonly StackPanel employeePanel = new() { Spacing = 7 };
    private readonly TextBlock employee = new()
    {
          Text = "All employees"
        , FontWeight = FontWeight.Bold
        , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
    };
    private readonly TextBox employeeSearch = new()
    {
          Watermark = "Name, code, TRN, or identifier"
        , MinWidth = 300
    };
    private readonly ListBox employeeResults = new()
    {
          MinHeight = 80
        , MaxHeight = 130
    };

    private readonly StackPanel productPanel = new() { Spacing = 6 };
    private readonly ComboBox product = new() { MinWidth = 320 };

    private readonly StackPanel groupingPanel = new() { Spacing = 6 };
    private readonly ComboBox grouping = new() { MinWidth = 180 };

    private readonly CheckBox includeVoided = new()
    {
        Content = "Include voided orders where applicable"
    };

    private readonly TextBlock status = new()
    {
          Foreground = new SolidColorBrush(Color.Parse("#687584"))
        , TextWrapping = TextWrapping.Wrap
    };

    private readonly Button preview = ActionButton("Preview");
    private readonly Button saveHtml = ActionButton("Save HTML");
    private readonly Button saveCsv = ActionButton("CSV");
    private readonly Button saveExcel = ActionButton("Excel");
    private readonly Button savePdf = ActionButton("PDF");

    private int? employeeId;

    public ReportCenterWindow
    (
          ICafeteriaReportService reports
        , ICustomerIdentificationService identification
        , IProductCatalogQuery products
        , IEnumerable<IReportExporter> exporters
        , CafeteriaReportKind initialKind
    )
    {
        this.reports = reports;
        this.identification = identification;
        this.products = products;
        this.exporters = exporters.ToDictionary(exporter => exporter.Format, StringComparer.OrdinalIgnoreCase);

        Title = "Reports - Cafeteria";
        Width = 920;
        Height = 700;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#F4F7FA"));

        DateTimeOffset today = DateTimeOffset.Now;
        this.from.SelectedDate = new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, today.Offset);
        this.to.SelectedDate = today;

        ReportChoice[] choices = this.reports.Catalog
            .Select(item => new ReportChoice(item))
            .ToArray();
        this.report.ItemsSource = choices;
        this.report.SelectedItem = choices.FirstOrDefault(choice => choice.Item.Kind == initialKind) ?? choices.FirstOrDefault();

        this.grouping.ItemsSource = Enum.GetValues<PurchaseGrouping>();
        this.grouping.SelectedItem = PurchaseGrouping.Day;

        Content = BuildLayout();
        WireEvents();
        UpdateReportUi();

        Opened += async (_, _) => await LoadProductsAsync();
    }

    private Control BuildLayout()
    {
        Grid root = new()
        {
              RowSpacing = 16
            , Margin = new Thickness(24)
        };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Border heading = new()
        {
              Background = new SolidColorBrush(Color.Parse("#182D44"))
            , Padding = new Thickness(22, 16)
            , CornerRadius = new CornerRadius(6)
            , Child = new StackPanel
            {
                  Spacing = 4
                , Children =
                {
                    new TextBlock
                    {
                          Text = "REPORT CENTER"
                        , FontSize = 24
                        , FontWeight = FontWeight.Bold
                        , Foreground = Brushes.White
                    },
                    new TextBlock
                    {
                          Text = "Operational, financial and audit reporting • Preview or export to Excel, PDF, CSV and HTML"
                        , Foreground = new SolidColorBrush(Color.Parse("#CDDDE8"))
                    }
                }
            }
        };
        root.Children.Add(heading);

        ScrollViewer scroll = new()
        {
              VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            , HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Border card = new()
        {
              Background = Brushes.White
            , Padding = new Thickness(22)
            , CornerRadius = new CornerRadius(6)
        };
        Grid form = new()
        {
              ColumnSpacing = 14
            , RowSpacing = 10
        };
        form.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(160)));
        form.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        int row = 0;
        AddRow(form, row++, "Report", this.report);
        AddRow(form, row++, string.Empty, this.description);

        Grid dates = new()
        {
            ColumnSpacing = 8
        };
        dates.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        dates.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        dates.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        dates.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        dates.Children.Add(new TextBlock { Text = "From", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(this.from, 1);
        dates.Children.Add(this.from);
        TextBlock toLabel = new() { Text = "To", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toLabel, 2);
        dates.Children.Add(toLabel);
        Grid.SetColumn(this.to, 3);
        dates.Children.Add(this.to);
        AddRow(form, row++, "Date range", dates);

        Button findEmployee = new()
        {
              Content = "Find Employee"
            , MinWidth = 115
        };
        findEmployee.Click += async (_, _) => await SearchEmployeesAsync();
        this.employeeSearch.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key != Avalonia.Input.Key.Enter)
            {
                return;
            }

            eventArgs.Handled = true;
            await SearchEmployeesAsync();
        };

        Button clearEmployee = new()
        {
              Content = "All Employees"
            , MinWidth = 110
        };
        clearEmployee.Click += (_, _) => ClearEmployee();

        Grid employeeSearchRow = new() { ColumnSpacing = 8 };
        employeeSearchRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        employeeSearchRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        employeeSearchRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        employeeSearchRow.Children.Add(this.employeeSearch);
        Grid.SetColumn(findEmployee, 1);
        employeeSearchRow.Children.Add(findEmployee);
        Grid.SetColumn(clearEmployee, 2);
        employeeSearchRow.Children.Add(clearEmployee);

        this.employeePanel.Children.Add(this.employee);
        this.employeePanel.Children.Add(employeeSearchRow);
        this.employeePanel.Children.Add(this.employeeResults);
        AddRow(form, row++, "Employee", this.employeePanel);

        this.productPanel.Children.Add(this.product);
        AddRow(form, row++, "Product", this.productPanel);

        this.groupingPanel.Children.Add(this.grouping);
        AddRow(form, row++, "Group totals", this.groupingPanel);

        AddRow(form, row++, "Options", this.includeVoided);
        AddRow(form, row++, string.Empty, this.status);

        this.employeeResults.SelectionChanged += (_, _) =>
        {
            if (this.employeeResults.SelectedItem is not EmployeeChoice choice)
            {
                return;
            }

            this.employeeId = choice.Customer.CustomerId;
            this.employee.Text = choice.Customer.Name;
        };

        card.Child = form;
        scroll.Content = card;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        StackPanel buttons = new()
        {
              Orientation = Orientation.Horizontal
            , HorizontalAlignment = HorizontalAlignment.Right
            , Spacing = 9
        };

        Button close = new()
        {
              Content = "Close"
            , MinWidth = 100
        };
        close.Click += (_, _) => Close();

        buttons.Children.Add(this.preview);
        buttons.Children.Add(this.saveExcel);
        buttons.Children.Add(this.savePdf);
        buttons.Children.Add(this.saveCsv);
        buttons.Children.Add(this.saveHtml);
        buttons.Children.Add(close);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        return root;
    }

    private static void AddRow(Grid form, int row, string label, Control control)
    {
        form.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock labelControl = new()
        {
              Text = label
            , FontWeight = FontWeight.Bold
            , Foreground = new SolidColorBrush(Color.Parse("#182D44"))
            , Margin = new Thickness(0, 7, 0, 0)
        };

        Grid.SetRow(labelControl, row);
        form.Children.Add(labelControl);

        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        form.Children.Add(control);
    }

    private void WireEvents()
    {
        this.report.SelectionChanged += (_, _) => UpdateReportUi();
        this.preview.Click += async (_, _) => await PreviewAsync();
        this.saveHtml.Click += async (_, _) => await SaveAsync("HTML");
        this.saveCsv.Click += async (_, _) => await SaveAsync("CSV");
        this.saveExcel.Click += async (_, _) => await SaveAsync("XLSX");
        this.savePdf.Click += async (_, _) => await SaveAsync("PDF");
    }

    private ReportCatalogItem SelectedCatalogItem
        => ((ReportChoice)this.report.SelectedItem!).Item;

    private void UpdateReportUi()
    {
        if (this.report.SelectedItem is not ReportChoice choice)
        {
            return;
        }

        ReportCatalogItem item = choice.Item;
        this.description.Text = item.Description;
        this.employeePanel.IsVisible = item.SupportsEmployeeFilter;
        this.productPanel.IsVisible = item.SupportsProductFilter;
        this.includeVoided.IsVisible = item.Kind is
            CafeteriaReportKind.ExecutiveSummary
            or CafeteriaReportKind.DailySales
            or CafeteriaReportKind.SalesTrend
            or CafeteriaReportKind.TenderMix
            or CafeteriaReportKind.ProductSales;
        this.groupingPanel.IsVisible = item.Kind is
            CafeteriaReportKind.PurchaseItemDetail
            or CafeteriaReportKind.PurchaseByPerson;

        if (item.Kind == CafeteriaReportKind.EmployeeStatement && this.to.SelectedDate is DateTimeOffset selected)
        {
            this.from.SelectedDate = new DateTimeOffset(selected.Year, selected.Month, 1, 0, 0, 0, selected.Offset);
            this.to.SelectedDate = new DateTimeOffset
            (
                  selected.Year
                , selected.Month
                , DateTime.DaysInMonth(selected.Year, selected.Month)
                , 0
                , 0
                , 0
                , selected.Offset
            );
        }
    }

    private async Task SearchEmployeesAsync()
    {
        string search = this.employeeSearch.Text?.Trim() ?? string.Empty;
        if (search.Length < 2)
        {
            this.status.Text = "Enter at least two characters to search for an employee.";
            return;
        }

        IReadOnlyList<CustomerSearchCandidate> candidates = await this.identification.SearchAsync(search, 30);
        EmployeeChoice[] choices = candidates
            .Where(candidate => candidate.IsEligibleForCredit)
            .Select(candidate => new EmployeeChoice(candidate))
            .ToArray();

        this.employeeResults.ItemsSource = choices;
        this.status.Text = choices.Length == 0
            ? "No eligible employees matched the search."
            : $"{choices.Length} employee(s) found.";
    }

    private void ClearEmployee()
    {
        this.employeeId = null;
        this.employee.Text = "All employees";
        this.employeeSearch.Text = string.Empty;
        this.employeeResults.ItemsSource = Array.Empty<EmployeeChoice>();
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            IReadOnlyList<ProductCatalogItem> items = await this.products.GetAllAsync();
            ProductChoice[] choices =
            [
                new ProductChoice(null, "All products"),
                .. items
                    .OrderBy(item => item.Name)
                    .Select(item => new ProductChoice(item.Id, item.Name))
            ];
            this.product.ItemsSource = choices;
            this.product.SelectedIndex = 0;
        }
        catch (Exception exception)
        {
            this.status.Text = $"Could not load product filter: {exception.Message}";
        }
    }

    private ReportFilter BuildFilter()
    {
        DateTimeOffset fromValue = this.from.SelectedDate ?? DateTimeOffset.Now;
        DateTimeOffset toValue = this.to.SelectedDate ?? DateTimeOffset.Now;
        DateOnly fromDate = DateOnly.FromDateTime(fromValue.DateTime);
        DateOnly toDate = DateOnly.FromDateTime(toValue.DateTime);

        if (toDate < fromDate)
        {
            throw new InvalidOperationException("The To date cannot be earlier than the From date.");
        }

        ReportCatalogItem item = SelectedCatalogItem;

        if (item.Kind == CafeteriaReportKind.EmployeeStatement && !this.employeeId.HasValue)
        {
            throw new InvalidOperationException("Employee statement requires an employee. Choose an employee first.");
        }

        if (item.Kind == CafeteriaReportKind.EmployeeStatement
            && (fromDate.Year != toDate.Year || fromDate.Month != toDate.Month))
        {
            throw new InvalidOperationException("Employee statement must be limited to one calendar month.");
        }

        int? productId = item.SupportsProductFilter && this.product.SelectedItem is ProductChoice productChoice
            ? productChoice.Id
            : null;

        PurchaseGrouping selectedGrouping = this.grouping.SelectedItem is PurchaseGrouping value
            ? value
            : PurchaseGrouping.Day;

        return new ReportFilter
        (
              fromDate
            , toDate
            , item.SupportsEmployeeFilter ? this.employeeId : null
            , productId
            , this.includeVoided.IsChecked == true
            , selectedGrouping
        );
    }

    private async Task<ReportDocument?> GenerateAsync()
    {
        try
        {
            ToggleBusy(true);
            this.status.Text = "Generating report...";
            ReportDocument document = await this.reports.GenerateAsync(SelectedCatalogItem.Kind, BuildFilter());
            this.status.Text = $"Generated {document.Title}.";
            return document;
        }
        catch (Exception exception)
        {
            this.status.Text = exception.Message;
            return null;
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task PreviewAsync()
    {
        ReportDocument? document = await GenerateAsync();
        if (document is null)
        {
            return;
        }

        if (!this.exporters.TryGetValue("HTML", out IReportExporter? exporter))
        {
            this.status.Text = "HTML report exporter is unavailable.";
            return;
        }

        string directory = IOPath.Combine(IOPath.GetTempPath(), "Cafeteria", "Reports");
        Directory.CreateDirectory(directory);
        string path = IOPath.Combine
        (
              directory
            , $"{SafeFileName(document.Title)}-{DateTime.Now:yyyyMMdd-HHmmss}.html"
        );

        this.status.Text = "Rendering preview...";
        byte[] bytes = await RenderExporterAsync(exporter, document);
        await File.WriteAllBytesAsync(path, bytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        this.status.Text = $"Opened {document.Title}.";
    }

    private async Task SaveAsync(string format)
    {
        ReportDocument? document = await GenerateAsync();
        if (document is null)
        {
            return;
        }

        if (!this.exporters.TryGetValue(format, out IReportExporter? exporter))
        {
            this.status.Text = $"{format} report exporter is unavailable.";
            return;
        }

        FilePickerFileType fileType = new(format switch
        {
            "HTML" => "HTML report",
            "CSV" => "CSV report",
            "XLSX" => "Excel workbook",
            "PDF" => "PDF document",
            _ => "Report"
        })
        {
            Patterns = [$"*{exporter.FileExtension}"]
        };

        IStorageFile? file = await StorageProvider.SaveFilePickerAsync
        (
            new FilePickerSaveOptions
            {
                  Title = $"Save {document.Title}"
                , SuggestedFileName = $"{SafeFileName(document.Title)}-{DateTime.Now:yyyyMMdd}{exporter.FileExtension}"
                , DefaultExtension = exporter.FileExtension.TrimStart('.')
                , FileTypeChoices = [fileType]
            }
        );

        if (file is null)
        {
            return;
        }

        ToggleBusy(true);

        try
        {
            this.status.Text = $"Rendering {format}...";
            byte[] bytes = await RenderExporterAsync(exporter, document);
            await using Stream stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
            {
                stream.SetLength(0);
            }

            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            this.status.Text = $"Saved {file.Name}.";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private static Task<byte[]> RenderExporterAsync(IReportExporter exporter, ReportDocument report)
    {
        return Task.Run
        (
            async () =>
            {
                await using MemoryStream buffer = new();
                await exporter.ExportAsync(report, buffer).ConfigureAwait(false);
                return buffer.ToArray();
            }
        );
    }

    private void ToggleBusy(bool busy)
    {
        this.preview.IsEnabled = !busy;
        this.saveHtml.IsEnabled = !busy;
        this.saveCsv.IsEnabled = !busy;
        this.saveExcel.IsEnabled = !busy;
        this.savePdf.IsEnabled = !busy;
    }

    private static string SafeFileName(string value)
    {
        char[] invalid = IOPath.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray())
            .Replace(' ', '-');
    }

    private static Button ActionButton(string text)
        => new()
        {
              Content = text
            , MinWidth = 94
            , MinHeight = 38
        };

    private sealed record ReportChoice(ReportCatalogItem Item)
    {
        public override string ToString() => Item.Name;
    }

    private sealed record ProductChoice(int? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record EmployeeChoice(CustomerSearchCandidate Customer)
    {
        public override string ToString()
            => string.IsNullOrWhiteSpace(this.Customer.Department)
                ? this.Customer.Name
                : $"{this.Customer.Name} — {this.Customer.Department}";
    }
}
