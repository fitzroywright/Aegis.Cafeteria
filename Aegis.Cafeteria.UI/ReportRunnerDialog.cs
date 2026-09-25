using System.Diagnostics;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Export;
using Aegis.Cafeteria.Reporting.Models;
using Aegis.Cafeteria.Infrastructure.Services;

namespace Aegis.Cafeteria.UI;

internal sealed class ReportRunnerDialog : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);

    private readonly ICafeteriaReportService reports;
    private readonly ICustomerIdentificationService identification;
    private readonly IProductCatalogQuery products;
    private readonly IReadOnlyDictionary<string, IReportExporter> exporters;
    private readonly CafeteriaServicesClient servicesClient;
    private readonly Func<Task<bool>> authorizeEmail;

    private readonly ComboBox report = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DateTimePicker from = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly DateTimePicker to = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly Label description = new() { AutoSize = true, ForeColor = Muted, MaximumSize = new Size(690, 0) };
    private readonly Panel employeePanel = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Label employee = new() { Text = "All employees", AutoSize = true, ForeColor = Navy, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    private readonly Button chooseEmployee = new() { Text = "Choose Employee", Width = 135, Height = 34 };
    private readonly Button clearEmployee = new() { Text = "All Employees", Width = 110, Height = 34 };
    private readonly Panel productPanel = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly ComboBox product = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly CheckBox includeVoided = new() { Text = "Include voided orders where applicable", AutoSize = true };
    private readonly ComboBox grouping = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Panel groupingPanel = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Label status = new() { AutoSize = true, ForeColor = Muted };
    private readonly Button preview = new() { Text = "Preview", Width = 110, Height = 40 };
    private readonly Button saveHtml = new() { Text = "Save HTML", Width = 110, Height = 40 };
    private readonly Button saveCsv = new() { Text = "CSV", Width = 92, Height = 40 };
    private readonly Button saveExcel = new() { Text = "Excel", Width = 100, Height = 40 };
    private readonly Button savePdf = new() { Text = "PDF", Width = 92, Height = 40 };
    private readonly Button emailReport = new() { Text = "Email Report", Width = 120, Height = 40, Visible = false };

    private int? employeeId;
    private IReadOnlyList<ProductCatalogItem> productItems = [];

    public ReportRunnerDialog(
        ICafeteriaReportService reports,
        ICustomerIdentificationService identification,
        IProductCatalogQuery products,
        IEnumerable<IReportExporter> exporters,
        CafeteriaReportKind initialKind,
        CafeteriaServicesClient servicesClient,
        Func<Task<bool>> authorizeEmail)
    {
        this.reports = reports;
        this.identification = identification;
        this.products = products;
        this.exporters = exporters.ToDictionary(x => x.Format, StringComparer.OrdinalIgnoreCase);
        this.servicesClient = servicesClient;
        this.authorizeEmail = authorizeEmail;

        Text = "Reports - Cafeteria";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(820, 600);
        Size = new Size(900, 650);
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10);

        var today = DateTime.Today;
        this.from.Value = new DateTime(today.Year, today.Month, 1);
        this.to.Value = today;
        if (initialKind == CafeteriaReportKind.DailySales)
        {
            this.from.Value = today;
        }

        foreach (var item in this.reports.Catalog)
            this.report.Items.Add(new ReportChoice(item));
        this.report.DisplayMember = nameof(ReportChoice.DisplayName);
        var initial = this.report.Items.Cast<ReportChoice>().FirstOrDefault(x => x.Item.Kind == initialKind);
        this.report.SelectedItem = initial ?? this.report.Items.Cast<object>().FirstOrDefault();

        this.grouping.DataSource = Enum.GetValues<PurchaseGrouping>();
        this.grouping.SelectedItem = PurchaseGrouping.Day;

        BuildLayout();
        WireEvents();
        UpdateReportUi();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(24)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var headingCard = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = Navy, Padding = new Padding(22, 15, 22, 12), Margin = new Padding(0, 0, 0, 18) };
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        heading.Controls.Add(new Label { Text = "REPORT CENTER", AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.White });
        heading.Controls.Add(new Label { Text = "Operational, financial and audit reporting • Preview or export to Excel, PDF, CSV and HTML", AutoSize = true, ForeColor = Color.FromArgb(205, 219, 232) });
        headingCard.Controls.Add(heading);
        root.Controls.Add(headingCard, 0, 0);

        var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(24) };
        var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 9 };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(form, 0, "Report", this.report);
        form.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 1);
        form.Controls.Add(this.description, 1, 1);

        var dates = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        dates.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        dates.Controls.Add(this.from);
        dates.Controls.Add(new Label { Text = "To", AutoSize = true, Margin = new Padding(20, 7, 8, 0) });
        dates.Controls.Add(this.to);
        AddRow(form, 2, "Date range", dates);

        var employeeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        employeeRow.Controls.Add(this.employee);
        employeeRow.Controls.Add(this.chooseEmployee);
        employeeRow.Controls.Add(this.clearEmployee);
        this.employeePanel.Controls.Add(employeeRow);
        AddRow(form, 3, "Employee", this.employeePanel);

        var productRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        productRow.Controls.Add(this.product);
        this.productPanel.Controls.Add(productRow);
        AddRow(form, 4, "Product", this.productPanel);

        this.groupingPanel.Controls.Add(this.grouping);
        AddRow(form, 5, "Group totals", this.groupingPanel);
        AddRow(form, 6, "Options", this.includeVoided);
        form.Controls.Add(new Label { Text = string.Empty, AutoSize = true }, 0, 7);
        form.Controls.Add(this.status, 1, 7);
        card.Controls.Add(form);
        root.Controls.Add(card, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 16, 0, 0) };
        var close = new Button { Text = "Close", Width = 100, Height = 40, DialogResult = DialogResult.Cancel };
        StyleActionButton(this.preview, Blue, Color.White);
        StyleActionButton(this.saveExcel, Green, Color.White);
        StyleActionButton(this.savePdf, Color.FromArgb(174, 48, 48), Color.White);
        StyleActionButton(this.saveCsv, Color.White, Navy);
        StyleActionButton(this.saveHtml, Color.White, Navy);
        buttons.Controls.Add(close);
        buttons.Controls.Add(this.saveHtml);
        buttons.Controls.Add(this.saveCsv);
        buttons.Controls.Add(this.savePdf);
        buttons.Controls.Add(this.emailReport);
        buttons.Controls.Add(this.saveExcel);
        buttons.Controls.Add(this.preview);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
        CancelButton = close;
    }


    private static void StyleActionButton(Button button, Color backColor, Color foreColor)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = backColor == Color.White ? Color.FromArgb(205, 214, 223) : backColor;
        button.FlatAppearance.BorderSize = 1;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static void AddRow(TableLayoutPanel form, int row, string label, Control control)
    {
        form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Navy, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 8, 12, 12) }, 0, row);
        control.Margin = new Padding(0, 4, 0, 12);
        form.Controls.Add(control, 1, row);
    }

    private void WireEvents()
    {
        this.report.SelectedIndexChanged += (_, _) => UpdateReportUi();
        this.chooseEmployee.Click += (_, _) => ChooseEmployee();
        this.clearEmployee.Click += (_, _) => ClearEmployee();
        this.preview.Click += async (_, _) => await RunPreviewAsync();
        this.saveHtml.Click += async (_, _) => await SaveAsync("HTML");
        this.saveCsv.Click += async (_, _) => await SaveAsync("CSV");
        this.saveExcel.Click += async (_, _) => await SaveAsync("XLSX");
        this.savePdf.Click += async (_, _) => await SaveAsync("PDF");
        this.emailReport.Click += async (_, _) => await EmailDailySalesAsync();
        Shown += async (_, _) => await LoadProductsAsync();
    }

    private ReportCatalogItem SelectedCatalogItem => ((ReportChoice)this.report.SelectedItem!).Item;

    private void UpdateReportUi()
    {
        if (this.report.SelectedItem is not ReportChoice choice) return;
        var item = choice.Item;
        this.description.Text = item.Description;
        this.employeePanel.Visible = item.SupportsEmployeeFilter;
        this.productPanel.Visible = item.SupportsProductFilter;
        this.includeVoided.Visible = item.Kind is CafeteriaReportKind.ExecutiveSummary or CafeteriaReportKind.DailySales or CafeteriaReportKind.SalesTrend or CafeteriaReportKind.TenderMix or CafeteriaReportKind.ProductSales;
        this.groupingPanel.Visible = item.Kind is CafeteriaReportKind.PurchaseItemDetail or CafeteriaReportKind.PurchaseByPerson;
        this.emailReport.Visible = item.Kind == CafeteriaReportKind.DailySales;

        if (item.Kind == CafeteriaReportKind.EmployeeStatement)
        {
            var date = this.to.Value.Date;
            this.from.Value = new DateTime(date.Year, date.Month, 1);
            this.to.Value = new DateTime(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        }
    }

    private void ChooseEmployee()
    {
        using var dialog = new CustomerSearchDialog(this.identification);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedCustomer is null) return;
        this.employeeId = dialog.SelectedCustomer.CustomerId;
        this.employee.Text = dialog.SelectedCustomer.Name;
    }

    private void ClearEmployee()
    {
        this.employeeId = null;
        this.employee.Text = "All employees";
    }

    private async Task LoadProductsAsync()
    {
        try
        {
            this.productItems = await this.products.GetAllAsync();
            var choices = new List<ProductChoice> { new(null, "All products") };
            choices.AddRange(this.productItems.OrderBy(x => x.Name).Select(x => new ProductChoice(x.Id, x.Name)));
            this.product.DataSource = choices;
            this.product.DisplayMember = nameof(ProductChoice.Name);
            this.product.ValueMember = nameof(ProductChoice.Id);
        }
        catch (Exception ex)
        {
            this.status.Text = $"Could not load product filter: {ex.Message}";
        }
    }

    private ReportFilter BuildFilter()
    {
        var item = SelectedCatalogItem;
        var from = DateOnly.FromDateTime(this.from.Value.Date);
        var to = DateOnly.FromDateTime(this.to.Value.Date);
        if (to < from) throw new InvalidOperationException("The To date cannot be earlier than the From date.");
        if (item.Kind == CafeteriaReportKind.EmployeeStatement && !this.employeeId.HasValue)
            throw new InvalidOperationException("Employee statement requires an employee. Choose an employee first.");
        if (item.Kind == CafeteriaReportKind.EmployeeStatement && (from.Year != to.Year || from.Month != to.Month))
            throw new InvalidOperationException("Employee statement must be limited to one calendar month.");

        int? productId = null;
        if (item.SupportsProductFilter && this.product.SelectedItem is ProductChoice p)
            productId = p.Id;
        var grouping = this.grouping.SelectedItem is PurchaseGrouping selectedGrouping ? selectedGrouping : PurchaseGrouping.Day;
        return new ReportFilter(from, to, item.SupportsEmployeeFilter ? this.employeeId : null, productId, this.includeVoided.Checked, grouping);
    }

    private async Task<ReportDocument?> GenerateAsync()
    {
        try
        {
            ToggleBusy(true);
            this.status.Text = "Generating report...";
            var report = await this.reports.GenerateAsync(SelectedCatalogItem.Kind, BuildFilter());
            this.status.Text = $"Generated {report.Title}.";
            return report;
        }
        catch (Exception ex)
        {
            this.status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Report Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private async Task RunPreviewAsync()
    {
        var report = await GenerateAsync();
        if (report is null) return;
        if (!this.exporters.TryGetValue("HTML", out var exporter))
        {
            MessageBox.Show(this, "HTML report exporter is unavailable.", "Report Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "Cafeteria", "Reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{SafeFileName(report.Title)}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        this.status.Text = "Rendering preview...";
        var bytes = await RenderExporterAsync(exporter, report);
        await File.WriteAllBytesAsync(path, bytes);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        this.status.Text = $"Opened {report.Title}.";
    }

    private async Task SaveAsync(string format)
    {
        var report = await GenerateAsync();
        if (report is null) return;
        if (!this.exporters.TryGetValue(format, out var exporter))
        {
            MessageBox.Show(this, $"{format} report exporter is unavailable.", "Report Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = $"Save {report.Title}",
            Filter = format switch
            {
                "HTML" => "HTML Report (*.html)|*.html",
                "CSV" => "CSV Report (*.csv)|*.csv",
                "XLSX" => "Excel Workbook (*.xlsx)|*.xlsx",
                "PDF" => "PDF Document (*.pdf)|*.pdf",
                _ => "All Files (*.*)|*.*"
            },
            FileName = $"{SafeFileName(report.Title)}-{DateTime.Now:yyyyMMdd}.{exporter.FileExtension.TrimStart('.')}",
            DefaultExt = exporter.FileExtension.TrimStart('.'),
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ToggleBusy(true);
        try
        {
            this.status.Text = $"Rendering {format}...";
            var bytes = await RenderExporterAsync(exporter, report);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            this.status.Text = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        finally
        {
            ToggleBusy(false);
        }
    }


    private async Task EmailDailySalesAsync()
    {
        ReportFilter filter = BuildFilter();
        if (SelectedCatalogItem.Kind != CafeteriaReportKind.DailySales)
        {
            return;
        }

        if (filter.From != filter.To)
        {
            MessageBox.Show(
                this,
                "Email Report requires a single Daily Sales report date. Set From and To to the same day.",
                "Email Daily Sales Report",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!await this.authorizeEmail())
        {
            return;
        }

        ManagerReportSettingsSnapshot settings = await this.servicesClient.GetManagerReportSettingsAsync();
        using DailySalesEmailDialog recipientsDialog = new(settings.EmailAddress);
        if (recipientsDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ReportDocument? report = await GenerateAsync();
        if (report is null)
        {
            return;
        }

        if (!this.exporters.TryGetValue("PDF", out IReportExporter? exporter))
        {
            MessageBox.Show(this, "PDF report exporter is unavailable.", "Email Daily Sales Report", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        ToggleBusy(true);
        try
        {
            this.status.Text = "Rendering and queueing Daily Sales email...";
            byte[] bytes = await RenderExporterAsync(exporter, report);
            string fileName = $"Cafeteria-Daily-Sales-{filter.From:yyyy-MM-dd}.pdf";

            await this.servicesClient.EmailDailySalesReportAsync(
                  filter.From
                , recipientsDialog.Recipients
                , fileName
                , bytes);

            this.status.Text = $"Daily Sales Report queued for {recipientsDialog.Recipients.Count} recipient(s).";
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private static Task<byte[]> RenderExporterAsync(IReportExporter exporter, ReportDocument report)
    {
        return Task.Run(async () =>
        {
            await using var buffer = new MemoryStream();
            await exporter.ExportAsync(report, buffer).ConfigureAwait(false);
            return buffer.ToArray();
        });
    }

    private void ToggleBusy(bool busy)
    {
        UseWaitCursor = busy;
        this.preview.Enabled = !busy;
        this.saveHtml.Enabled = !busy;
        this.saveCsv.Enabled = !busy;
        this.saveExcel.Enabled = !busy;
        this.savePdf.Enabled = !busy;
        this.emailReport.Enabled = !busy;
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Replace(' ', '-');
    }

    private sealed record ReportChoice(ReportCatalogItem Item)
    {
        public string DisplayName => Item.Name;
        public override string ToString() => DisplayName;
    }

    private sealed record ProductChoice(int? Id, string Name)
    {
        public override string ToString() => Name;
    }
}
