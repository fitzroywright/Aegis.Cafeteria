using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Services;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Export;
using Aegis.Cafeteria.Reporting.Models;
using Aegis.Cafeteria.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Diagnostics;

namespace Aegis.Cafeteria.UI;

public sealed class MainForm : Form
{
    private static readonly Color Navy = Color.FromArgb(24, 45, 68);
    private static readonly Color Blue = Color.FromArgb(31, 111, 190);
    private static readonly Color Surface = Color.White;
    private static readonly Color Canvas = Color.FromArgb(244, 247, 250);
    private static readonly Color Muted = Color.FromArgb(104, 117, 132);
    private static readonly Color Green = Color.FromArgb(23, 119, 76);
    private static readonly Color Red = Color.FromArgb(176, 52, 52);

    private readonly ICustomerIdentificationService identification;
    private readonly ICustomerAccountQuery accountQuery;
    private readonly IProductCatalogQuery products;
    private readonly PosCheckoutService checkout;
    private readonly ILogger<MainForm> logger;
    private readonly ICustomerReferenceCacheStatus cacheStatusProvider;
    private readonly CafeteriaHealthOptions healthOptions;
    private readonly CafeteriaPersistenceOptions persistenceOptions;
    private readonly CafeteriaUiOptions uiOptions;
    private readonly IOperatorSession operatorSession;
    private readonly IOperatorAuthenticator operatorAuthenticator;
    private readonly IManagerAuthorizationRegistry managerAuthorization;
    private readonly RefundService refundService;
    private readonly CustomerPaymentWorkflow customerPayments;
    private readonly PosVoidService voidService;
    private readonly IOrderQuery orderQuery;
    private readonly IRecentTransactionQuery recentTransactions;
    private readonly ICafeteriaPersistenceStore persistenceStore;
    private readonly IServiceProvider services;
    private readonly IDisposable accountChangeSubscription;
    private readonly System.Windows.Forms.Timer cacheStatusTimer = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer customerInactivityTimer = new() { Interval = 1_000 };
    private SplitContainer? mainSplit;
    private Control? transactionWorkspace;
    private Panel? recentTransactionsPanel;
    private readonly PosActivityMessageFilter activityMessageFilter;
    private DateTimeOffset lastActivityUtc = DateTimeOffset.UtcNow;
    private CustomerDisplayForm? customerDisplay;
    private Screen? customerDisplayScreen;

    private readonly TextBox lookup = new() { PlaceholderText = "Tap card or enter Code, TRN, or Name", Width = 420, BorderStyle = BorderStyle.FixedSingle };
    private readonly Button find = ActionButton("Find", Blue, 88);
    private readonly Button cashCustomer = ActionButton("Cash Customer", Green, 125);

    private readonly Panel idlePanel = new() { Dock = DockStyle.Fill, BackColor = Surface };
    private readonly Label todayNetSales = DashboardValueLabel();
    private readonly Label todayTransactions = DashboardValueLabel();
    private readonly Label todayRefunds = DashboardValueLabel();
    private readonly Label todayDashboardStatus = new() { AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9) };
    private readonly WeekdayBarChartControl weeklySalesByDay = new();
    private readonly PieChartControl todayItemsByValue = new();
    private readonly Panel customerPanel = new() { Dock = DockStyle.Fill, BackColor = Surface, Visible = false };
    private readonly PictureBox photo = new() { SizeMode = PictureBoxSizeMode.Zoom, Width = 132, Height = 154, BackColor = Color.FromArgb(235, 239, 243) };
    private readonly Label customerName = new() { AutoSize = true, Font = new Font("Segoe UI", 21, FontStyle.Bold), ForeColor = Navy, MaximumSize = new Size(245, 0) };
    private readonly Label eligibility = new() { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(9, 4, 9, 4) };
    private readonly Label period = InfoLabel();
    private readonly Label fundingStatus = new() { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 8, 0, 0) };
    private readonly Label creditLimit = MoneyLabel();
    private readonly Label subsidyRemaining = MoneyLabel();
    private readonly Label nonSubsidyRemaining = MoneyLabel();
    private readonly Label available = MoneyLabel(true);
    private readonly Label carriedDebt = MoneyLabel();
    private readonly Label currentCreditDebt = MoneyLabel();
    private readonly Label customerLiability = MoneyLabel();
    private readonly Label customerPaymentsValue = MoneyLabel();
    private readonly Label employeeAmountDue = MoneyLabel();
    private readonly DataGridView transactions = Grid();

    private readonly TextBox productSearch = new() { PlaceholderText = "Search products", Dock = DockStyle.Top, Height = 34 };
    private readonly DataGridView productGrid = Grid();
    private readonly DataGridView cartGrid = Grid();
    private readonly Label cartTotal = new() { AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Navy, Text = "$0.00" };
    private readonly Label cartCaption = new() { AutoSize = true, Text = "ORDER TOTAL", ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
    private readonly Button checkoutButton = ActionButton("Complete Sale", Green, 190, 50);
    private readonly Button voidButton = ActionButton("Void Current Sale", Red, 150, 50);
    private readonly Button clearButton = ActionButton("New Customer", Color.FromArgb(100, 112, 126), 130, 50);
    private readonly Button refundButton = ActionButton("Refund Selected", Red, 135, 32);
    private readonly Button customerPaymentButton = ActionButton("Customer Payment...", Green, 158, 38);
    private readonly Label voidAvailability = new() { AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };

    private readonly Label posStatus = StatusLabel("POS Ready");
    private readonly Label operatorStatus = StatusLabel("Operator: not signed in");
    private readonly Label syncStatus = StatusLabel("Local data ready");
    private readonly LinkLabel cacheStatus = new()
    {
        Text = "● Employee cache: checking...",
        AutoSize = true,
        LinkBehavior = LinkBehavior.NeverUnderline,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Margin = new Padding(0, 0, 18, 0)
    };

    private CustomerAccountView? currentCustomer;
    private readonly BindingSource transactionSource = new();
    private readonly BindingSource productSource = new();
    private readonly BindingSource cartSource = new();
    private readonly BindingSource recentTransactionSource = new();
    private readonly DataGridView recentTransactionGrid = Grid();
    private readonly List<CartRow> cart = [];

    public MainForm(
        ICustomerIdentificationService identification,
        ICustomerAccountQuery accountQuery,
        IProductCatalogQuery products,
        PosCheckoutService checkout,
        ICustomerReferenceCacheStatus cacheStatusProvider,
        CafeteriaHealthOptions healthOptions,
        CafeteriaPersistenceOptions persistenceOptions,
        CafeteriaUiOptions uiOptions,
        IOperatorSession operatorSession,
        IOperatorAuthenticator operatorAuthenticator,
        IManagerAuthorizationRegistry managerAuthorization,
        RefundService refundService,
        CustomerPaymentWorkflow customerPayments,
        PosVoidService voidService,
        IOrderQuery orderQuery,
        IRecentTransactionQuery recentTransactions,
        ICafeteriaPersistenceStore persistenceStore,
        ICustomerAccountChangeNotifier accountChangeNotifier,
        IServiceProvider services,
        ILogger<MainForm> logger)
    {
        this.identification = identification;
        this.accountQuery = accountQuery;
        this.products = products;
        this.checkout = checkout;
        this.cacheStatusProvider = cacheStatusProvider;
        this.healthOptions = healthOptions;
        this.persistenceOptions = persistenceOptions;
        this.uiOptions = uiOptions;
        this.activityMessageFilter = new PosActivityMessageFilter(RegisterActivity);
        this.operatorSession = operatorSession;
        this.operatorAuthenticator = operatorAuthenticator;
        this.managerAuthorization = managerAuthorization;
        this.refundService = refundService;
        this.customerPayments = customerPayments;
        this.voidService = voidService;
        this.orderQuery = orderQuery;
        this.recentTransactions = recentTransactions;
        this.persistenceStore = persistenceStore;
        this.accountChangeSubscription = accountChangeNotifier.Subscribe(OnCustomerAccountChangedAsync);
        this.services = services;
        this.logger = logger;

        Text = "Cafeteria";
        Icon = Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath);
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1200, 760);
        Font = new Font("Segoe UI", 10);
        BackColor = Canvas;
        KeyPreview = true;

        ConfigureGrids();
        BuildLayout();

        Shown += async (_, _) =>
        {
            ApplyMainSplitLayout();
            InitializeCustomerDisplay();
            await LoadProductsAsync();
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
            await UpdateCacheStatusAsync();
            UpdateOperatorStatus();
            this.cacheStatusTimer.Start();
            if (this.uiOptions.CustomerInactivityTimeoutSeconds > 0)
            {
                System.Windows.Forms.Application.AddMessageFilter(this.activityMessageFilter);
                RegisterActivity();
                this.customerInactivityTimer.Start();
            }
            FocusCustomerLookup();
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;

            var answer = MessageBox.Show(
                this,
                "Are you sure you want to close Cafeteria?",
                "Confirm Close",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
                e.Cancel = true;
        };


        FormClosed += (_, _) =>
        {
            this.cacheStatusTimer.Stop();
            this.customerInactivityTimer.Stop();
            System.Windows.Forms.Application.RemoveMessageFilter(this.activityMessageFilter);
            this.accountChangeSubscription.Dispose();
            if (this.customerDisplay is not null)
            {
                this.customerDisplay.Close();
                this.customerDisplay.Dispose();
                this.customerDisplay = null;
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape || this.currentCustomer is null) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            CancelActiveTransaction();
        };

        this.find.Click += async (_, _) => await IdentifyAsync();
        this.lookup.TextChanged += (_, _) => RegisterActivity();
        this.cashCustomer.Click += (_, _) => ShowCustomer(CashCustomerAccountViewFactory.Create(DateOnly.FromDateTime(DateTime.Today)));
        this.lookup.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await IdentifyAsync();
        };
        this.productSearch.TextChanged += async (_, _) => await LoadProductsAsync(this.productSearch.Text);
        this.customerInactivityTimer.Tick += (_, _) => CheckCustomerInactivity();
        this.productGrid.CellDoubleClick += (_, e) => AddSelectedProduct(e.RowIndex);
        this.cartGrid.CellDoubleClick += (_, e) => RemoveCartRow(e.RowIndex);
        this.checkoutButton.Click += async (_, _) => await CheckoutAsync();
        this.voidButton.Click += async (_, _) => await VoidCurrentSaleAsync();
        this.refundButton.Click += async (_, e) => await RefundSelectedAsync();
        this.customerPaymentButton.Click += async (_, _) => await RecordCustomerPaymentAsync();
        this.clearButton.Click += (_, _) => ClearCustomer();
        this.cacheStatus.Click += async (_, _) => await ShowCacheStatusAsync();
        this.cacheStatusTimer.Tick += async (_, _) => await UpdateCacheStatusAsync();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var menu = BuildMenu();
        MainMenuStrip = menu;
        root.Controls.Add(menu, 0, 0);
        root.Controls.Add(BuildHeader(), 0, 1);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BackColor = Canvas,
            BorderStyle = BorderStyle.None
        };
        this.mainSplit = body;
        body.SizeChanged += (_, _) => ApplyMainSplitLayout();
        body.Panel1.Padding = new Padding(18, 18, 8, 18);
        body.Panel2.Padding = new Padding(8, 18, 18, 18);

        var customerHost = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        customerHost.Controls.Add(this.customerPanel);
        customerHost.Controls.Add(this.idlePanel);
        BuildIdlePanel();
        BuildCustomerPanel();
        body.Panel1.Controls.Add(customerHost);
        body.Panel2.Controls.Add(BuildOrderPanel());
        root.Controls.Add(body, 0, 2);
        root.Controls.Add(BuildStatusBar(), 0, 3);
        Controls.Add(root);
    }

    private void ApplyMainSplitLayout()
    {
        if (this.mainSplit is null || this.mainSplit.IsDisposed)
            return;

        int width = this.mainSplit.ClientSize.Width;
        if (width <= this.mainSplit.SplitterWidth + 2)
            return;

        const int desiredTransactionPanel1Min = 420;
        const int desiredTransactionPanel2Min = 420;

        int available = width - this.mainSplit.SplitterWidth;
        if (available < 100)
            return;

        int desiredLeft = available / 2;
        int desiredPanel1Min = desiredTransactionPanel1Min;
        int desiredPanel2Min = desiredTransactionPanel2Min;
        bool fullMinimumsFit = available >= desiredPanel1Min + desiredPanel2Min;

        int panel1Min = fullMinimumsFit
            ? desiredPanel1Min
            : Math.Min(desiredPanel1Min, Math.Max(25, available / 3));
        int panel2Min = fullMinimumsFit
            ? desiredPanel2Min
            : Math.Min(desiredPanel2Min, Math.Max(25, available - panel1Min - 1));

        int minimumDistance = panel1Min;
        int maximumDistance = Math.Max(minimumDistance, available - panel2Min);
        int distance = Math.Clamp(desiredLeft, minimumDistance, maximumDistance);

        this.mainSplit.Panel1MinSize = 0;
        this.mainSplit.Panel2MinSize = 0;
        this.mainSplit.SplitterDistance = Math.Clamp(distance, 0, Math.Max(0, available));
        this.mainSplit.Panel1MinSize = Math.Min(panel1Min, this.mainSplit.SplitterDistance);
        this.mainSplit.Panel2MinSize = Math.Min(panel2Min, Math.Max(0, available - this.mainSplit.SplitterDistance));
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Top,
            BackColor = Color.White,
            ForeColor = Navy,
            Font = new Font("Segoe UI", 10)
        };

        var reports = new ToolStripMenuItem("Reports") { Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        var reportCenter = ReportMenuItem("Report Center...   Preview • Excel • PDF", CafeteriaReportKind.ExecutiveSummary);
        reportCenter.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        reportCenter.ForeColor = Blue;
        reports.DropDownItems.Add(reportCenter);
        reports.DropDownItems.Add(new ToolStripSeparator());

        var overview = ReportCategory("Overview & KPIs");
        overview.DropDownItems.Add(ReportMenuItem("Executive Summary", CafeteriaReportKind.ExecutiveSummary));

        var sales = ReportCategory("Sales & Purchases");
        sales.DropDownItems.Add(ReportMenuItem("Daily Sales", CafeteriaReportKind.DailySales));
        sales.DropDownItems.Add(ReportMenuItem("Sales Trend", CafeteriaReportKind.SalesTrend));
        sales.DropDownItems.Add(ReportMenuItem("Tender Mix", CafeteriaReportKind.TenderMix));
        sales.DropDownItems.Add(ReportMenuItem("Product Sales", CafeteriaReportKind.ProductSales));
        sales.DropDownItems.Add(new ToolStripSeparator());
        sales.DropDownItems.Add(ReportMenuItem("Itemized Purchases by Customer", CafeteriaReportKind.PurchaseItemDetail));
        sales.DropDownItems.Add(ReportMenuItem("Purchases Summarized by Person", CafeteriaReportKind.PurchaseByPerson));

        var employee = ReportCategory("Employee & Credit");
        employee.DropDownItems.Add(ReportMenuItem("Canteen Summary", CafeteriaReportKind.CanteenSummary));
        employee.DropDownItems.Add(ReportMenuItem("Employee Statement", CafeteriaReportKind.EmployeeStatement));
        employee.DropDownItems.Add(ReportMenuItem("Monthly Allocations", CafeteriaReportKind.MonthlyAllocations));
        employee.DropDownItems.Add(ReportMenuItem("Subsidy Utilization", CafeteriaReportKind.SubsidyUtilization));
        employee.DropDownItems.Add(ReportMenuItem("Customer Responsibility", CafeteriaReportKind.CustomerResponsibility));
        employee.DropDownItems.Add(ReportMenuItem("Outstanding Debt", CafeteriaReportKind.OutstandingDebt));

        var audit = ReportCategory("Audit & Reconciliation");
        audit.DropDownItems.Add(ReportMenuItem("Refund && Void Audit", CafeteriaReportKind.RefundVoidAudit));
        audit.DropDownItems.Add(ReportMenuItem("Additional Credit Audit", CafeteriaReportKind.AdditionalCreditAudit));
        audit.DropDownItems.Add(ReportMenuItem("External System Reconciliation", CafeteriaReportKind.ExternalSystemReconciliation));

        reports.DropDownItems.Add(overview);
        reports.DropDownItems.Add(sales);
        reports.DropDownItems.Add(employee);
        reports.DropDownItems.Add(audit);
        menu.Items.Add(reports);

        ToolStripMenuItem administration = new("Administration")
        {
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };

        ToolStripMenuItem cafeteriaAdministration = new("Price Maintenance...");
        cafeteriaAdministration.Click += async (_, _) => await OpenCafeteriaAdministrationAsync();
        administration.DropDownItems.Add(cafeteriaAdministration);

        ToolStripMenuItem emailPreferences = new("Employee Email Preferences...");
        emailPreferences.Click += (_, _) => OpenEmployeeEmailPreferences();
        administration.DropDownItems.Add(emailPreferences);

        ToolStripMenuItem automaticOperations = new("Automatic Operations...");
        automaticOperations.Click += async (_, _) => await OpenAutomaticOperationsAsync();
        administration.DropDownItems.Add(automaticOperations);

        administration.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem diagnostics = new("Diagnostics");
        diagnostics.Click += (_, _) => OpenEngineeringDiagnostics();
        administration.DropDownItems.Add(diagnostics);
        menu.Items.Add(administration);
        return menu;
    }

    private void OpenEmployeeEmailPreferences()
    {
        CafeteriaServicesClient servicesClient = this.services.GetRequiredService<CafeteriaServicesClient>();
        using EmployeeEmailPreferencesDialog dialog = new(this.identification, servicesClient);
        dialog.ShowDialog(this);
        RegisterActivity();
    }

    private async Task OpenAutomaticOperationsAsync()
    {
        OperatorIdentity? manager = await AuthorizeManagerAsync("Automatic Cafeteria operation override");
        if (manager is null)
        {
            return;
        }

        CafeteriaServicesClient servicesClient = this.services.GetRequiredService<CafeteriaServicesClient>();
        using AutomaticOperationsDialog dialog = new(servicesClient);
        dialog.ShowDialog(this);
        RegisterActivity();
    }

    private void OpenEngineeringDiagnostics()
    {
        CafeteriaServicesOptions cafeteriaServices = this.services.GetRequiredService<CafeteriaServicesOptions>();
        string diagnosticsUrl = cafeteriaServices.BaseUrl.TrimEnd('/') + "/engineering/diagnostics";
        Process.Start
        (
            new ProcessStartInfo
            {
                  FileName = diagnosticsUrl
                , UseShellExecute = true
            }
        );
    }

    private async Task OpenCafeteriaAdministrationAsync()
    {
        OperatorIdentity? manager = await AuthorizeManagerAsync("Cafeteria price maintenance");

        if (manager is null)
        {
            return;
        }

        IProductCatalogMaintenance maintenance = new AuthorizedProductCatalogMaintenance
        (
              this.services.GetRequiredService<IProductCatalogMaintenance>()
            , this.managerAuthorization
            , manager.UserId
        );
        using CafeteriaAdministrationDialog dialog = new(maintenance);
        dialog.ShowDialog(this);
        await LoadProductsAsync();
    }

    private static ToolStripMenuItem ReportCategory(string text) => new(text)
    {
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        ForeColor = Navy
    };

    private ToolStripMenuItem ReportMenuItem(string text, CafeteriaReportKind kind)
    {
        var metadata = CafeteriaReportCatalog.All.Single(x => x.Kind == kind);
        var item = new ToolStripMenuItem(text)
        {
            ToolTipText = metadata.Description,
            Padding = new Padding(4, 3, 4, 3)
        };
        item.Click += (_, _) => OpenReportRunner(kind);
        return item;
    }

    private void OpenReportRunner(CafeteriaReportKind kind)
    {
        var reportService = this.services.GetRequiredService<ICafeteriaReportService>();
        var exporters = this.services.GetServices<IReportExporter>();
        CafeteriaServicesClient servicesClient = this.services.GetRequiredService<CafeteriaServicesClient>();
        using var dialog = new ReportRunnerDialog(
              reportService
            , this.identification
            , this.products
            , exporters
            , kind
            , servicesClient
            , async () => await AuthorizeManagerAsync("Email Daily Sales Report") is not null);
        dialog.ShowDialog(this);
        RegisterActivity();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Navy, Padding = new Padding(20, 14, 20, 14) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(new Label { Text = "CAFETERIA", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold) });
        brand.Controls.Add(new Label { Text = "Cafeteria", AutoSize = true, ForeColor = Color.FromArgb(193, 210, 225), Font = new Font("Segoe UI", 9) });
        header.Controls.Add(brand, 0, 0);

        var identify = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Padding = new Padding(0, 5, 0, 0) };
        identify.Controls.Add(this.lookup);
        identify.Controls.Add(this.find);
        identify.Controls.Add(this.cashCustomer);
        header.Controls.Add(identify, 1, 0);
        return header;
    }

    private void BuildIdlePanel()
    {
        this.idlePanel.Padding = new Padding(26);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "CAFETERIA SALES DASHBOARD",
            AutoSize = true,
            ForeColor = Navy,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Today's KPIs with this week's Monday–Friday sales trend.",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font("Segoe UI", 10),
            Margin = new Padding(0, 0, 0, 18)
        }, 0, 1);

        var metrics = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3 };
        metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metrics.Controls.Add(DashboardMetricCard("NET SALES", this.todayNetSales), 0, 0);
        metrics.Controls.Add(DashboardMetricCard("TRANSACTIONS", this.todayTransactions), 0, 1);
        metrics.Controls.Add(DashboardMetricCard("REFUNDS", this.todayRefunds), 0, 2);
        root.Controls.Add(metrics, 0, 2);

        var dashboardBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 8, 0, 0) };
        dashboardBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        dashboardBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, MinimumSize = new Size(0, 225) };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        this.weeklySalesByDay.Dock = DockStyle.Fill;
        this.weeklySalesByDay.Margin = new Padding(0, 0, 6, 0);
        this.todayItemsByValue.Dock = DockStyle.Fill;
        this.todayItemsByValue.Margin = new Padding(6, 0, 0, 0);
        charts.Controls.Add(this.weeklySalesByDay, 0, 0);
        charts.Controls.Add(this.todayItemsByValue, 1, 0);
        dashboardBody.Controls.Add(charts, 0, 0);

        var readyStack = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        readyStack.Controls.Add(new Label { Text = "Ready for customer", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Navy });
        readyStack.Controls.Add(new Label { Text = "Scan a card, enter an identifier, or press Find with an empty search box to browse customers.", AutoSize = true, MaximumSize = new Size(430, 0), ForeColor = Muted, Font = new Font("Segoe UI", 9) });
        dashboardBody.Controls.Add(readyStack, 0, 1);
        root.Controls.Add(dashboardBody, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 16, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(this.todayDashboardStatus, 0, 0);
        var openReport = ActionButton("Open Daily Sales Report", Blue, 185, 38);
        openReport.Click += (_, _) => OpenReportRunner(CafeteriaReportKind.DailySales);
        footer.Controls.Add(openReport, 1, 0);
        root.Controls.Add(footer, 0, 4);

        this.idlePanel.Controls.Add(root);
    }

    private static Control DashboardMetricCard(string caption, Label value)
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            BackColor = Color.FromArgb(244, 247, 250),
            Padding = new Padding(16, 10, 16, 8),
            Margin = new Padding(0, 0, 0, 10)
        };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        stack.Controls.Add(value);
        card.Controls.Add(stack);
        return card;
    }

    private void BuildCustomerPanel()
    {
        this.customerPanel.Padding = new Padding(22);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var identity = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 16) };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identity.Controls.Add(this.photo, 0, 0);
        var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 0, 0, 0) };
        info.Controls.Add(this.customerName);
        info.Controls.Add(this.eligibility);
        info.Controls.Add(this.period);
        info.Controls.Add(this.fundingStatus);
        identity.Controls.Add(info, 1, 0);
        root.Controls.Add(identity, 0, 0);

        var highlight = new Panel { Dock = DockStyle.Top, Height = 106, BackColor = Color.FromArgb(234, 243, 252), Padding = new Padding(18, 14, 18, 12), Margin = new Padding(0, 0, 0, 12) };
        var availableStack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        availableStack.Controls.Add(new Label { Text = "AVAILABLE TO SPEND", AutoSize = true, ForeColor = Blue, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        availableStack.Controls.Add(this.available);
        highlight.Controls.Add(availableStack);
        root.Controls.Add(highlight, 0, 1);

        var finances = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 2, 0, 14) };
        finances.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        finances.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        AddFinancialRow(finances, "Monthly cafeteria credit", this.creditLimit);
        AddFinancialRow(finances, "Company subsidy remaining", this.subsidyRemaining);
        AddFinancialRow(finances, "Employee-funded remaining", this.nonSubsidyRemaining);
        AddFinancialRow(finances, "Debt brought forward", this.carriedDebt);
        AddFinancialRow(finances, "Credit sales this month", this.currentCreditDebt);
        AddFinancialRow(finances, "Customer Responsibility (before payments)", this.customerLiability);
        AddFinancialRow(finances, "Customer Payments", this.customerPaymentsValue);
        AddFinancialRow(finances, "Employee Amount Due", this.employeeAmountDue);
        root.Controls.Add(finances, 0, 2);

        var paymentAction = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Color.FromArgb(240, 248, 243), Padding = new Padding(12, 9, 12, 9), Margin = new Padding(0, 0, 0, 10) };
        paymentAction.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paymentAction.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        paymentAction.Controls.Add(new Label { Text = "CUSTOMER PAYMENT", AutoSize = true, ForeColor = Green, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 0);
        paymentAction.Controls.Add(new Label { Text = "Record money received from the employee to reduce the Employee Amount Due shown above.", AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Muted, Font = new Font("Segoe UI", 9) }, 0, 1);
        paymentAction.Controls.Add(this.customerPaymentButton, 1, 0);
        paymentAction.SetRowSpan(this.customerPaymentButton, 2);
        root.Controls.Add(paymentAction, 0, 3);

        this.transactions.RowTemplate.Height = 28;
        this.transactions.MinimumSize = new Size(0, this.transactions.ColumnHeadersHeight + (5 * this.transactions.RowTemplate.Height) + 4);

        var history = new Panel
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 38 + this.transactions.MinimumSize.Height)
        };
        var historyHeader = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2 };
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        historyHeader.Controls.Add(new Label { Text = "THIS MONTH", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Navy, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 0);
        historyHeader.Controls.Add(this.refundButton, 1, 0);
        history.Controls.Add(this.transactions);
        history.Controls.Add(historyHeader);
        root.Controls.Add(history, 0, 4);
        this.customerPanel.Controls.Add(root);
    }

    private Control BuildOrderPanel()
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(22) };

        var workspace = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1 };
        this.transactionWorkspace = workspace;
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var products = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 30, 0, 0) };
        products.Controls.Add(this.productGrid);
        products.Controls.Add(this.productSearch);
        products.Controls.Add(new Label { Text = "PRODUCTS", Dock = DockStyle.Top, Height = 28, ForeColor = Navy, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        workspace.Controls.Add(products, 0, 0);

        var cart = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 30, 0, 0) };
        cart.Controls.Add(this.cartGrid);
        cart.Controls.Add(new Label { Text = "CURRENT ORDER  •  Double-click an item to remove", Dock = DockStyle.Top, Height = 28, ForeColor = Navy, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        workspace.Controls.Add(cart, 0, 1);

        var total = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 14, 0, 6) };
        total.Controls.Add(this.cartCaption);
        total.Controls.Add(this.cartTotal);
        workspace.Controls.Add(total, 0, 2);

        var hint = new Label { Text = "Employee Card, cash, and approved Employee Credit can be combined at checkout.", AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, 10) };
        workspace.Controls.Add(hint, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 62,
            MinimumSize = new Size(520, 62),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4)
        };
        actions.Controls.Add(this.checkoutButton);
        actions.Controls.Add(this.voidButton);
        actions.Controls.Add(this.clearButton);
        var actionHost = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, RowCount = 2, ColumnCount = 1 };
        actionHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actionHost.Controls.Add(this.voidAvailability, 0, 0);
        actionHost.Controls.Add(actions, 0, 1);
        workspace.Controls.Add(actionHost, 0, 4);

        var recent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(0, 28, 0, 6),
            Visible = this.uiOptions.RecentTransactionCount > 0
        };
        this.recentTransactionsPanel = recent;
        recent.Controls.Add(this.recentTransactionGrid);
        recent.Controls.Add(new Label { Text = "RECENT TRANSACTIONS - TODAY", Dock = DockStyle.Top, Height = 32, ForeColor = Navy, Font = new Font("Segoe UI", 11, FontStyle.Bold) });

        card.Controls.Add(workspace);
        card.Controls.Add(recent);
        recent.BringToFront();
        return card;
    }

    private Control BuildStatusBar()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, BackColor = Color.FromArgb(234, 238, 242), Padding = new Padding(18, 8, 18, 8) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        left.Controls.Add(this.operatorStatus);
        left.Controls.Add(this.posStatus);
        left.Controls.Add(this.syncStatus);
        left.Controls.Add(this.cacheStatus);
        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(new Label { Text = "Secure identification • Name search", AutoSize = true, ForeColor = Muted, Anchor = AnchorStyles.Right }, 1, 0);
        return bar;
    }

    private async Task UpdateCacheStatusAsync()
    {
        try
        {
            var status = await this.cacheStatusProvider.GetAsync();
            var age = status.Age(DateTimeOffset.UtcNow);
            var ageMinutes = age?.TotalMinutes;

            if (status.RefreshInProgress)
            {
                this.cacheStatus.Text = $"● Employee cache: refreshing • {status.EmployeeCount:N0} employees";
                this.cacheStatus.LinkColor = Blue;
                return;
            }

            if (status.EmployeeCount == 0 || !status.CacheUpdatedAtUtc.HasValue)
            {
                this.cacheStatus.Text = "● Employee cache: EMPTY";
                this.cacheStatus.LinkColor = Red;
                return;
            }

            var ageText = FormatCacheAge(age);
            this.cacheStatus.Text = $"● Employee cache: {status.EmployeeCount:N0} • {ageText}";
            this.cacheStatus.LinkColor = ageMinutes >= this.healthOptions.EmployeeCacheCriticalMinutes
                ? Red
                : ageMinutes >= this.healthOptions.EmployeeCacheWarningMinutes
                    ? Color.DarkOrange
                    : Green;

            if (!string.IsNullOrWhiteSpace(status.LastError))
            {
                this.cacheStatus.Text += " • last refresh failed";
                if (this.cacheStatus.LinkColor == Green) this.cacheStatus.LinkColor = Color.DarkOrange;
            }
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not read employee cache status");
            this.cacheStatus.Text = "● Employee cache: status unavailable";
            this.cacheStatus.LinkColor = Red;
        }
    }

    private async Task ShowCacheStatusAsync()
    {
        var refresh = this.services.GetService<ReferenceDataRefreshService>();
        using var dialog = new CacheStatusDialog(
            this.cacheStatusProvider,
            refresh is null ? null : async () =>
            {
                await refresh.RefreshAsync(DateOnly.FromDateTime(DateTime.Today));
                await LoadProductsAsync();
                return 0;
            });
        dialog.ShowDialog(this);
        await UpdateCacheStatusAsync();
    }

    private static string FormatCacheAge(TimeSpan? age)
    {
        if (!age.HasValue) return "age unknown";
        if (age.Value.TotalMinutes < 1) return "updated just now";
        if (age.Value.TotalHours < 1) return $"updated {(int)age.Value.TotalMinutes}m ago";
        if (age.Value.TotalDays < 1) return $"updated {age.Value.TotalHours:0.0}h ago";
        return $"updated {age.Value.TotalDays:0.0}d ago";
    }

    private void ConfigureGrids()
    {
        this.transactions.DataSource = this.transactionSource;
        this.productGrid.DataSource = this.productSource;
        this.cartGrid.DataSource = this.cartSource;
        this.recentTransactionGrid.DataSource = this.recentTransactionSource;
        this.transactions.AutoGenerateColumns = true;

        this.productGrid.AutoGenerateColumns = false;
        this.productGrid.Columns.Clear();
        this.productGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ProductCatalogItem.Name),
            HeaderText = "Item",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 180
        });
        this.productGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ProductCatalogItem.Price),
            HeaderText = "Price",
            Width = 110,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "C2"
            }
        });

        this.recentTransactionGrid.AutoGenerateColumns = false;
        this.recentTransactionGrid.Columns.Clear();
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Time", HeaderText = "Time", Width = 72 });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Customer", HeaderText = "Customer", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150 });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Total", HeaderText = "Total", Width = 95, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight, Format = "C2" } });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Tender", HeaderText = "Tender", Width = 115 });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Cashier", HeaderText = "Cashier", Width = 110 });

        this.cartGrid.AutoGenerateColumns = true;

        SyncGridHeaderAlignment(this.productGrid);
        SyncGridHeaderAlignment(this.recentTransactionGrid);
        SyncGridHeaderAlignment(this.transactions);
        SyncGridHeaderAlignment(this.cartGrid);
    }

    private static void SyncGridHeaderAlignment(DataGridView grid)
    {
        void Apply()
        {
            foreach (DataGridViewColumn column in grid.Columns)
            {
                var alignment = column.DefaultCellStyle.Alignment;
                if (alignment == DataGridViewContentAlignment.NotSet)
                    alignment = grid.DefaultCellStyle.Alignment;
                if (alignment == DataGridViewContentAlignment.NotSet)
                    alignment = DataGridViewContentAlignment.MiddleLeft;

                column.HeaderCell.Style.Alignment = alignment;
            }
        }

        Apply();
        grid.ColumnAdded += (_, _) => Apply();
        grid.DataBindingComplete += (_, _) => Apply();
    }

    private async Task BrowseCustomersAsync()
    {
        using var dialog = new CustomerSearchDialog(this.identification);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedCustomer is null) return;
        var account = await this.identification.LoadAsync(dialog.SelectedCustomer.CustomerId);
        if (account is not null) ShowCustomer(account);
    }

    private async Task IdentifyAsync()
    {
        var value = this.lookup.Text.Trim();
        if (value.Length == 0)
        {
            await BrowseCustomersAsync();
            FocusCustomerLookup();
            return;
        }

        try
        {
            UseWaitCursor = true;
            var result = await this.identification.ResolveAsync(value, 50);
            CustomerAccountView? account = result.Account;

            if (account is null && result.Candidates.Count > 0)
            {
                var chosen = ChooseCustomer(result.Candidates, value);
                if (chosen is null) return;
                account = await this.identification.LoadAsync(chosen.CustomerId);
            }

            if (account is null)
            {
                MessageBox.Show(this, "No matching employee was found in the local employee cache.", "Cafeteria", MessageBoxButtons.OK, MessageBoxIcon.Information);
                FocusCustomerLookup(selectAll: true);
                return;
            }

            this.lookup.Clear();
            ShowCustomer(account);
            this.productSearch.Focus();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Universal customer identification failed");
            MessageBox.Show(this, ex.Message, "Customer Identification Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            FocusCustomerLookup(selectAll: true);
        }
        finally { UseWaitCursor = false; }
    }

    private void FocusCustomerLookup(bool selectAll = false)
    {
        if (this.currentCustomer is not null) return;
        this.lookup.Focus();
        if (selectAll) this.lookup.SelectAll();
    }

    private CustomerSearchCandidate? ChooseCustomer(IReadOnlyList<CustomerSearchCandidate> candidates, string? searchText = null)
    {
        using var dialog = new CustomerSearchDialog(this.identification, candidates, searchText);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedCustomer : null;
    }

    private void ShowCustomer(CustomerAccountView account)
    {
        RegisterActivity();
        this.currentCustomer = account;
        UpdateRecentTransactionsVisibility();
        this.idlePanel.Visible = false;
        this.customerPanel.Visible = true;
        this.customerPanel.BringToFront();

        this.customerName.Text = account.Customer.Name;
        this.eligibility.Text = CafeteriaDisplayText.Eligibility(account);
        this.eligibility.ForeColor = account.Customer.IsEligibleForCredit ? Green : account.Customer.IsCashCustomer ? Blue : Red;
        this.eligibility.BackColor = account.Customer.IsEligibleForCredit ? Color.FromArgb(226, 246, 236) : account.Customer.IsCashCustomer ? Color.FromArgb(234, 243, 252) : Color.FromArgb(255, 232, 232);
        this.period.Text = CafeteriaDisplayText.Period(account);
        var funding = account.Funding;
        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit)
        {
            this.fundingStatus.Text = account.Customer.IsCashCustomer
                ? "● Cash customer  •  Cash tender only"
                : "● Not eligible for cafeteria credit  •  Cash purchases only";
            this.fundingStatus.ForeColor = Red;
            this.fundingStatus.BackColor = Color.FromArgb(255, 232, 232);
            SetFinancialValuesUnavailable();
        }
        else
        {
            if (funding?.IsAuthoritative == true)
            {
                this.fundingStatus.Text = "● Funding current";
                this.fundingStatus.ForeColor = Green;
                this.fundingStatus.BackColor = Color.FromArgb(226, 246, 236);
            }
            else
            {
                this.fundingStatus.Text = $"● Funding unavailable  •  {funding?.StatusMessage ?? "Reference data not loaded"}";
                this.fundingStatus.ForeColor = Red;
                this.fundingStatus.BackColor = Color.FromArgb(255, 232, 232);
            }
            this.creditLimit.Text = Money(account.FinancialStatus.CreditLimit);
            this.subsidyRemaining.Text = Money(account.FinancialStatus.SubsidyAmountRemaining);
            this.nonSubsidyRemaining.Text = Money(account.FinancialStatus.NonSubsidyAmountRemaining);
            this.available.Text = Money(account.FinancialStatus.TotalAvailableToSpend);
            this.carriedDebt.Text = Money(account.FinancialStatus.CarriedForwardDebt);
            this.currentCreditDebt.Text = Money(account.FinancialStatus.CurrentCreditSaleDebt);
            this.customerLiability.Text = Money(account.FinancialStatus.GrossCustomerLiability);
            this.customerPaymentsValue.Text = Money(account.FinancialStatus.CustomerPayments);
            this.employeeAmountDue.Text = Money(account.FinancialStatus.EmployeeAmountDue);
        }
        this.transactionSource.DataSource = account.Transactions.Select(x => new
        {
            Date = x.OccurredAt.LocalDateTime,
            x.Kind,
            x.Description,
            Amount = x.Amount,
            Account = x.CafeteriaAccountAmount,
            Cash = x.CashAmount,
            x.ReferenceId
        }).ToList();
        if (this.transactions.Columns[nameof(CustomerMonthlyTransactionView.ReferenceId)] is DataGridViewColumn referenceColumn)
            referenceColumn.Visible = false;
        if (this.transactions.Columns["Kind"] is DataGridViewColumn transactionTypeColumn)
            transactionTypeColumn.HeaderText = "Transaction Type";
        if (this.transactions.Columns["Amount"] is DataGridViewColumn saleTotalColumn)
            saleTotalColumn.HeaderText = "Sale Total";
        if (this.transactions.Columns["Account"] is DataGridViewColumn employeeCardColumn)
            employeeCardColumn.HeaderText = CafeteriaDisplayText.EmployeeCard;
        SyncGridHeaderAlignment(this.transactions);
        SetPhoto(account.Customer.Photo);

        if (account.Customer.IsCashCustomer)
        {
            this.syncStatus.Text = "● Cash sale ready";
            this.syncStatus.ForeColor = Green;
        }
        else
        {
            this.syncStatus.Text = "● Cafeteria Services synchronization active";
            this.syncStatus.ForeColor = Green;
        }
        CustomerPaymentEligibility paymentEligibility = CustomerPaymentPolicy.Evaluate(account);
        this.customerPaymentButton.Enabled = paymentEligibility.Allowed;
        UpdateCheckoutState();
        _ = UpdateCustomerDisplayAsync(account);
    }

    private void InitializeCustomerDisplay()
    {
        var selected = CustomerDisplayPolicy.SelectCustomerMonitor(
            Screen.AllScreens.Select(screen => new CustomerDisplayMonitor(screen.DeviceName, screen.Primary)));
        this.customerDisplayScreen = selected is null
            ? null
            : Screen.AllScreens.First(screen => screen.DeviceName == selected.DeviceName);
        if (this.customerDisplayScreen is null)
        {
            this.logger.LogInformation("No secondary monitor detected. Customer-facing display is disabled.");
            return;
        }

        this.customerDisplay = new CustomerDisplayForm();
        this.customerDisplay.PlaceOn(this.customerDisplayScreen);
        this.logger.LogInformation(
            "Customer-facing display enabled on monitor {DeviceName} at {Bounds}",
            this.customerDisplayScreen.DeviceName,
            this.customerDisplayScreen.Bounds);
    }

    private async Task UpdateCustomerDisplayAsync(CustomerAccountView account)
    {
        if (this.customerDisplay is null || this.customerDisplayScreen is null) return;

        try
        {
            IReadOnlyList<CustomerMonthlyTransactionView> history = account.Customer.IsCashCustomer
                ? Array.Empty<CustomerMonthlyTransactionView>()
                : await this.accountQuery.GetTransactionsAsync(
                    account.Customer.Id,
                    account.Period.Year,
                    account.Period.Month);

            if (this.currentCustomer?.Customer.Id != account.Customer.Id) return;

            void ShowOnDisplay()
            {
                if (this.customerDisplay is null || this.customerDisplayScreen is null) return;
                this.customerDisplay.PlaceOn(this.customerDisplayScreen);
                this.customerDisplay.ShowCustomer(account, history, account.Period.Year, account.Period.Month);
                if (!this.customerDisplay.Visible) this.customerDisplay.Show();
                this.customerDisplay.BringToFront();
            }

            if (InvokeRequired) BeginInvoke((Action)ShowOnDisplay);
            else ShowOnDisplay();
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not update the customer-facing display for customer {CustomerId}", account.Customer.Id);
        }
    }

    private void SetFinancialValuesUnavailable()
    {
        foreach (var label in new[] { this.creditLimit, this.subsidyRemaining, this.nonSubsidyRemaining, this.available, this.carriedDebt, this.currentCreditDebt, this.customerLiability, this.customerPaymentsValue, this.employeeAmountDue })
            label.Text = "—";
    }

    private async Task LoadProductsAsync(string? search = null)
    {
        try
        {
            var rows = string.IsNullOrWhiteSpace(search)
                ? await this.products.GetAllAsync()
                : await this.products.SearchAsync(search, 50);
            this.productSource.DataSource = rows.ToList();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Product catalog load failed");
            this.syncStatus.Text = "● Product catalog unavailable";
            this.syncStatus.ForeColor = Red;
        }
    }

    private void AddSelectedProduct(int rowIndex)
    {
        if (rowIndex < 0 || this.productGrid.Rows[rowIndex].DataBoundItem is not ProductCatalogItem product) return;
        var existing = this.cart.FirstOrDefault(x => x.ProductId == product.Id);
        if (existing is null) this.cart.Add(new CartRow(product.Id, product.Name, product.Price, 1));
        else existing.Quantity++;
        RefreshCart();
    }

    private void RemoveCartRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= this.cart.Count) return;
        this.cart.RemoveAt(rowIndex);
        RefreshCart();
    }

    private void RefreshCart()
    {
        this.cartSource.DataSource = this.cart.Select(x => new { x.ProductName, x.UnitPrice, x.Quantity, x.Total }).ToList();
        this.cartTotal.Text = Money(this.cart.Sum(x => x.Total));
        UpdateCheckoutState();
    }

    private async Task LoadIdleDashboardAsync()
    {
        try
        {
            ICafeteriaReportService reportService = this.services.GetRequiredService<ICafeteriaReportService>();
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            CafeteriaReport report = await reportService.GenerateAsync(
                CafeteriaReportKind.DailySales,
                new ReportFilter(today, today));

            decimal Metric(string label)
                => report.Metrics.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase))?.Value ?? 0m;

            this.todayNetSales.Text = Money(Metric("Net sales"));
            this.todayTransactions.Text = Metric("Transactions").ToString("N0");
            this.todayRefunds.Text = Money(Metric("Refunds"));

            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            DateOnly monday = today.AddDays(-daysSinceMonday);
            DateOnly friday = monday.AddDays(4);
            CafeteriaReport weeklyReport = await reportService.GenerateAsync(
                CafeteriaReportKind.DailySales,
                new ReportFilter(monday, friday));
            IReadOnlyList<IReadOnlyDictionary<string, object?>> weeklyRows = weeklyReport.Tables
                .FirstOrDefault(x => string.Equals(x.Title, "Daily detail", StringComparison.OrdinalIgnoreCase))?
                .Rows ?? [];

            List<(string Label, decimal Value)> weekdayValues = [];
            for (int dayOffset = 0; dayOffset < 5; dayOffset++)
            {
                DateOnly day = monday.AddDays(dayOffset);
                IReadOnlyDictionary<string, object?>? row = weeklyRows.FirstOrDefault(x =>
                {
                    object? dateValue = x.GetValueOrDefault("date");
                    return dateValue is DateOnly reportDate && reportDate == day;
                });
                decimal value = row is null ? 0m : Convert.ToDecimal(row.GetValueOrDefault("net") ?? 0m);
                weekdayValues.Add((day.ToString("dddd"), value));
            }
            this.weeklySalesByDay.SetData("SALES VALUE BY DAY — THIS WEEK", weekdayValues, "C0");

            CafeteriaReport productReport = await reportService.GenerateAsync(
                CafeteriaReportKind.ProductSales,
                new ReportFilter(today, today));
            IReadOnlyList<IReadOnlyDictionary<string, object?>> productRows = productReport.Tables
                .FirstOrDefault(x => string.Equals(x.Title, "Product detail", StringComparison.OrdinalIgnoreCase))?
                .Rows ?? [];
            var productSales = productRows.Select(row => new
            {
                Name = Convert.ToString(row.GetValueOrDefault("product")) ?? "Unknown",
                Value = Convert.ToDecimal(row.GetValueOrDefault("sales") ?? 0m)
            }).ToArray();

            this.todayItemsByValue.SetData("ITEMS SOLD — VALUE", DashboardPieBuckets(productSales.Select(x => (x.Name, x.Value))), "C0");
            this.todayDashboardStatus.Text = CafeteriaDisplay.Updated(DateTime.Now);
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not load the idle sales dashboard");
            this.todayNetSales.Text = "—";
            this.todayTransactions.Text = "—";
            this.todayRefunds.Text = "—";
            this.weeklySalesByDay.SetData(
                "SALES VALUE BY DAY — THIS WEEK",
                new[] { ("Monday", 0m), ("Tuesday", 0m), ("Wednesday", 0m), ("Thursday", 0m), ("Friday", 0m) },
                "C0");
            this.todayItemsByValue.SetData("ITEMS SOLD — VALUE", [], "C0");
            this.todayDashboardStatus.Text = "Dashboard temporarily unavailable";
        }
    }

    private static IReadOnlyList<(string Label, decimal Value)> DashboardPieBuckets(IEnumerable<(string Label, decimal Value)> source)
    {
        var ordered = source.Where(x => x.Value > 0m).OrderByDescending(x => x.Value).ToArray();
        if (ordered.Length <= 7) return ordered;
        return ordered.Take(7).Append(("Other", ordered.Skip(7).Sum(x => x.Value))).ToArray();
    }

    private async Task LoadRecentTransactionsAsync()
    {
        if (this.uiOptions.RecentTransactionCount <= 0)
        {
            this.recentTransactionSource.DataSource = null;
            return;
        }

        try
        {
            var rows = await this.recentTransactions.GetTodayAsync(this.uiOptions.RecentTransactionCount);
            this.recentTransactionSource.DataSource = rows.Select(x => new
            {
                x.OrderId,
                Time = x.CompletedAt.LocalDateTime.ToString("HH:mm"),
                Customer = x.CustomerName,
                x.Total,
                Tender = x.TenderSummary,
                Cashier = x.CashierUserId
            }).ToList();
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not load recent POS transactions");
        }
    }

    private async Task CheckoutAsync()
    {
        if (this.currentCustomer is null || this.cart.Count == 0) return;
        decimal total = this.cart.Sum(x => x.Total);
        TenderCustomerContext tenderContext = TenderCustomerPolicy.Evaluate(this.currentCustomer);
        using TenderDialog dialog = new
        (
            new TenderDialogOptions
            (
                  total
                , tenderContext.AvailableToSpend
                , tenderContext.SubsidyRemaining
                , tenderContext.CreditSalesAllowed
            )
        );
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            this.checkoutButton.Enabled = false;
            OperatorIdentity operatorIdentity = RequireOperator();
            PosCheckoutRequest request = new(
                this.currentCustomer,
                this.cart.Select(x => new PosOrderLine(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity)).ToArray(),
                dialog.CafeteriaAccountAmount,
                dialog.CashAmount,
                dialog.CreditAmount,
                operatorIdentity.UserId,
                this.persistenceOptions.TerminalId);
            await this.checkout.CompleteAsync(request);
            this.cart.Clear();
            RefreshCart();
            if (!this.currentCustomer.Customer.IsCashCustomer)
            {
                this.currentCustomer = await this.identification.LoadAsync(this.currentCustomer.Customer.Id);
                if (this.currentCustomer is not null) ShowCustomer(this.currentCustomer);
            }
            else
            {
                ShowCustomer(CashCustomerAccountViewFactory.Create(DateOnly.FromDateTime(DateTime.Today)));
            }
            this.posStatus.Text = "● Sale completed";
            this.posStatus.ForeColor = Green;
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Checkout failed for customer {CustomerId}", this.currentCustomer.Customer.Id);
            MessageBox.Show(this, ex.Message, "Checkout Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { UpdateCheckoutState(); }
    }

    private OperatorIdentity RequireOperator()
        => this.operatorSession.Current ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required.");

    private void UpdateOperatorStatus()
    {
        var current = this.operatorSession.Current;
        this.operatorStatus.Text = current is null
            ? "● Operator: not signed in"
            : $"● Operator: {current.DisplayName} ({current.UserId})" + (current.IsManager ? " • Manager" : string.Empty);
        this.operatorStatus.ForeColor = current is null ? Red : Green;
        UpdateCheckoutState();
    }

    private async Task<OperatorIdentity?> AuthorizeManagerAsync(string action)
    {
        var current = this.operatorSession.Current;
        if (current?.IsManager == true)
        {
            this.managerAuthorization.Grant(current);
            return current;
        }

        using var dialog = new ManagerAuthorizationDialog(this.operatorAuthenticator, this.managerAuthorization, action);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.AuthorizedManager : null;
    }

    private async Task VoidCurrentSaleAsync()
    {
        if (this.currentCustomer is null || this.cart.Count == 0) return;
        var manager = await AuthorizeManagerAsync("Void current sale");
        if (manager is null) return;
        using var reasonDialog = new ReasonDialog("Void Sale", "Reason for voiding this sale:");
        if (reasonDialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            await this.voidService.VoidCurrentSaleAsync(
                this.currentCustomer,
                this.cart.Select(x => new PosOrderLine(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity)).ToArray(),
                manager.UserId,
                this.persistenceOptions.TerminalId,
                reasonDialog.Reason);
            this.cart.Clear();
            RefreshCart();
            this.posStatus.Text = $"● Sale voided by {manager.UserId}";
            this.posStatus.ForeColor = Color.DarkOrange;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Void failed for operator {ManagerUserId}", manager.UserId);
            MessageBox.Show(this, ex.Message, "Void Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RecordCustomerPaymentAsync()
    {
        if (this.currentCustomer is null) return;
        CustomerPaymentEligibility eligibility = CustomerPaymentPolicy.Evaluate(this.currentCustomer);
        if (!eligibility.Allowed)
        {
            MessageBox.Show(this, eligibility.Reason ?? "Customer payment is not available.", "Customer Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        decimal due = eligibility.MaximumPayment;

        using var dialog = new CustomerPaymentDialog(this.currentCustomer.Customer.Name, due);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            this.customerPaymentButton.Enabled = false;
            var operatorId = this.operatorSession.Current?.UserId ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required.");
            await this.customerPayments.RecordAsync(this.currentCustomer, dialog.Amount, dialog.PaymentMethod, operatorId, dialog.Reference);
            var customerId = this.currentCustomer.Customer.Id;
            var refreshed = await this.identification.LoadAsync(customerId);
            if (refreshed is not null) ShowCustomer(refreshed);
            this.posStatus.Text = $"Customer payment recorded: {dialog.Amount:C2}";
            this.posStatus.ForeColor = Green;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Customer payment failed for customer {CustomerId}", this.currentCustomer.Customer.Id);
            MessageBox.Show(this, ex.Message, "Customer Payment", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (this.currentCustomer is not null)
                this.customerPaymentButton.Enabled = CustomerPaymentPolicy.Evaluate(this.currentCustomer).Allowed;
        }
    }

    private Task OnCustomerAccountChangedAsync
    (
        int customerId
    )
    {
        if (this.currentCustomer?.Customer.Id != customerId)
        {
            return Task.CompletedTask;
        }

        if (IsDisposed || Disposing)
        {
            return Task.CompletedTask;
        }

        BeginInvoke
        (
            async () =>
            {
                try
                {
                    CustomerAccountView? refreshed = await this.identification.LoadAsync(customerId);

                    if (refreshed is not null)
                    {
                        ShowCustomer(refreshed);
                    }
                }
                catch (Exception exception)
                {
                    this.logger.LogWarning
                    (
                          exception
                        , "Could not refresh displayed customer {CustomerId}"
                        , customerId
                    );
                }
            }
        );

        return Task.CompletedTask;
    }

    private async Task RefundSelectedAsync()
    {
        if (this.currentCustomer is null) return;
        var order = await SelectRefundOrderAsync();
        if (order is null) return;
        var orderId = order.Id;

        RefundEligibility refundEligibility = RefundEligibilityPolicy.Evaluate(order);
        if (!refundEligibility.Allowed)
        {
            MessageBox.Show(this, refundEligibility.Reason ?? "The selected order is not refundable.", "Refund", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var refundDialog = new RefundDialog(order);
        if (refundDialog.ShowDialog(this) != DialogResult.OK) return;

        var manager = await AuthorizeManagerAsync("Refund completed sale");
        if (manager is null) return;
        string defaultReason = RefundReasonGenerator.Generate(order, refundDialog.SelectedItems);
        using ReasonDialog reasonDialog = new("Refund Sale", "Reason for refund:", defaultReason);
        if (reasonDialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var accountBeforeRefund = this.currentCustomer;
            var refund = this.refundService.Refund(
                order,
                refundDialog.SelectedItems,
                refundDialog.CafeteriaAccountAmount,
                refundDialog.CashAmount,
                manager.UserId,
                DateTimeOffset.Now,
                reasonDialog.Reason);
            await this.persistenceStore.SaveOrderAsync(order);

            if (!accountBeforeRefund.Customer.IsCashCustomer)
            {
                this.currentCustomer = await this.identification.LoadAsync(accountBeforeRefund.Customer.Id);
                if (this.currentCustomer is not null) ShowCustomer(this.currentCustomer);
            }
            this.posStatus.Text = $"● Refund completed by {manager.UserId}";
            this.posStatus.ForeColor = Color.DarkOrange;
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Refund failed for order {OrderId} by {ManagerUserId}", orderId, manager.UserId);
            MessageBox.Show(this, ex.Message, "Refund Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task<Order?> SelectRefundOrderAsync()
    {
        if (this.currentCustomer is null) return null;

        if (!this.currentCustomer.Customer.IsCashCustomer && this.transactions.CurrentRow?.DataBoundItem is { } selectedRow)
        {
            Type rowType = selectedRow.GetType();
            string? kind = rowType.GetProperty("Kind")?.GetValue(selectedRow)?.ToString();
            object? referenceValue = rowType.GetProperty("ReferenceId")?.GetValue(selectedRow);

            if (string.Equals(kind, "Refund", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show
                (
                      this
                    , "Refund entries cannot be refunded. Select the original purchase instead."
                    , "Refund"
                    , MessageBoxButtons.OK
                    , MessageBoxIcon.Information
                );
                return null;
            }

            if (string.Equals(kind, "Purchase", StringComparison.OrdinalIgnoreCase) && referenceValue is Guid orderId)
            {
                return await this.orderQuery.GetAsync(orderId);
            }
        }

        var recent = await this.orderQuery.GetRecentCompletedAsync(this.currentCustomer.Customer.Id, 50);
        var refundable = recent
            .Where(x => RefundEligibilityPolicy.Evaluate(x).Allowed)
            .Take(5)
            .ToArray();
        if (refundable.Length == 0)
        {
            MessageBox.Show(this, "No refundable completed sales were found for this customer.", "Refund", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        using var dialog = new RecentOrdersDialog(
            refundable,
            this.currentCustomer.Customer.IsCashCustomer ? "Last 5 Refundable Cash Sales" : "Last 5 Refundable Customer Sales");
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedOrder : null;
    }

    private void RegisterActivity()
    {
        this.lastActivityUtc = DateTimeOffset.UtcNow;
    }

    private void CheckCustomerInactivity()
    {
        if (this.uiOptions.CustomerInactivityTimeoutSeconds <= 0 || this.currentCustomer is null) return;
        var timeout = TimeSpan.FromSeconds(this.uiOptions.CustomerInactivityTimeoutSeconds);
        if (DateTimeOffset.UtcNow - this.lastActivityUtc < timeout) return;

        this.logger.LogInformation(
            "Clearing customer {CustomerId} after {TimeoutSeconds} seconds of POS inactivity",
            this.currentCustomer.Customer.Id,
            this.uiOptions.CustomerInactivityTimeoutSeconds);
        ClearCustomer();
        RegisterActivity();
    }

    private sealed class PosActivityMessageFilter(Action onActivity) : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int WmMouseMove = 0x0200;
        private const int WmLButtonDown = 0x0201;
        private const int WmRButtonDown = 0x0204;
        private const int WmMButtonDown = 0x0207;
        private const int WmMouseWheel = 0x020A;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg is WmKeyDown or WmSysKeyDown or WmMouseMove or WmLButtonDown or WmRButtonDown or WmMButtonDown or WmMouseWheel)
                onActivity();
            return false;
        }
    }

    private void CancelActiveTransaction()
    {
        if (this.currentCustomer is null) return;

        ClearCustomer();
        this.posStatus.Text = "● Sale cancelled";
        this.posStatus.ForeColor = Color.DarkOrange;
    }

    private void ClearCustomer()
    {
        this.currentCustomer = null;
        UpdateRecentTransactionsVisibility();
        this.cart.Clear();
        RefreshCart();
        this.transactionSource.DataSource = null;
        this.customerPaymentButton.Enabled = false;
        this.customerPanel.Visible = false;
        this.idlePanel.Visible = true;
        this.idlePanel.BringToFront();
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        this.customerDisplay?.ClearCustomer();
        this.posStatus.Text = "● POS Ready";
        this.posStatus.ForeColor = Green;
        FocusCustomerLookup();
    }

    private void UpdateRecentTransactionsVisibility()
    {
        if (this.recentTransactionsPanel is null || this.transactionWorkspace is null) return;

        var showRecent = this.uiOptions.RecentTransactionCount > 0 && this.currentCustomer is null;
        this.recentTransactionsPanel.Visible = showRecent;
        this.transactionWorkspace.Visible = !showRecent;
        if (showRecent) this.recentTransactionsPanel.BringToFront();
        else this.transactionWorkspace.BringToFront();
        ApplyMainSplitLayout();
    }

    private void UpdateCheckoutState()
    {
        var hasCustomer = this.currentCustomer is not null;
        var hasOrder = hasCustomer && this.cart.Count > 0;
        var canAct = this.operatorSession.IsAuthenticated && hasOrder;
        this.checkoutButton.Enabled = canAct;
        this.voidButton.Enabled = canAct;
        this.refundButton.Enabled = this.operatorSession.IsAuthenticated && hasCustomer;

        if (!this.operatorSession.IsAuthenticated)
        {
            this.voidAvailability.Text = "VOID CURRENT SALE: sign in as an operator first.";
            this.voidAvailability.ForeColor = Muted;
        }
        else if (!hasCustomer)
        {
            this.voidAvailability.Text = "VOID CURRENT SALE: select a customer and add an item first.";
            this.voidAvailability.ForeColor = Muted;
        }
        else if (!hasOrder)
        {
            this.voidAvailability.Text = "VOID CURRENT SALE becomes available after the first item is added.";
            this.voidAvailability.ForeColor = Muted;
        }
        else
        {
            this.voidAvailability.Text = "VOID AVAILABLE • Voids the current uncompleted sale • Manager approval and reason required.";
            this.voidAvailability.ForeColor = Red;
        }
    }

    private void SetPhoto(byte[]? bytes)
    {
        this.photo.Image?.Dispose();
        this.photo.Image = null;
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using var stream = new MemoryStream(bytes);
            using var temp = Image.FromStream(stream);
            this.photo.Image = new Bitmap(temp);
        }
        catch (Exception ex) { this.logger.LogWarning(ex, "Invalid customer photo for customer {CustomerId}", this.currentCustomer?.Customer.Id); }
    }

    private static string Money(decimal value) => CafeteriaDisplay.Money(value);

    private static Button ActionButton(string text, Color backColor, int width, int height = 36) => new()
    {
        Text = text,
        Width = width,
        Height = height,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10, FontStyle.Bold),
        Cursor = Cursors.Hand,
        Margin = new Padding(5, 0, 0, 0)
    };

    private static Label InfoLabel() => new() { AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9), Margin = new Padding(0, 5, 0, 0) };
    private static Label DashboardValueLabel() => new()
    {
        AutoSize = true,
        Text = "—",
        ForeColor = Navy,
        Font = new Font("Segoe UI", 20, FontStyle.Bold),
        Margin = new Padding(0, 2, 0, 0)
    };

    private static Label MoneyLabel(bool emphasized = false) => new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        Text = "$0.00",
        ForeColor = emphasized ? Navy : Color.FromArgb(42, 53, 65),
        Font = new Font("Segoe UI", emphasized ? 25 : 11, emphasized ? FontStyle.Bold : FontStyle.Regular)
    };

    private static Label StatusLabel(string text) => new()
    {
        Text = "● " + text,
        AutoSize = true,
        ForeColor = Green,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Margin = new Padding(0, 0, 18, 0)
    };

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        RowHeadersVisible = false,
        BorderStyle = BorderStyle.None,
        BackgroundColor = Color.White,
        GridColor = Color.FromArgb(231, 235, 239),
        RowTemplate = { Height = 34 },
        EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 36,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(238, 242, 246),
            ForeColor = Navy,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            SelectionBackColor = Color.FromArgb(238, 242, 246),
            SelectionForeColor = Navy
        },
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(42, 53, 65),
            SelectionBackColor = Color.FromArgb(220, 235, 249),
            SelectionForeColor = Navy
        }
    };

    private static void AddFinancialRow(TableLayoutPanel table, string caption, Label value)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Muted, Padding = new Padding(0, 5, 0, 5) }, 0, row);
        table.Controls.Add(value, 1, row);
    }

    private sealed class CartRow(int productId, string productName, decimal unitPrice, int quantity)
    {
        public int ProductId { get; } = productId;
        public string ProductName { get; } = productName;
        public decimal UnitPrice { get; } = unitPrice;
        public int Quantity { get; set; } = quantity;
        public decimal Total => UnitPrice * Quantity;
    }
}
