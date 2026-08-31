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
    private readonly PieChartControl todayItemsByQuantity = new();
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
        this.refundButton.Click += async (_, _) => await RefundSelectedAsync();
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
        this.Controls.Add(root);

        root.Controls.Add(BuildMenu(), 0, 0);
        root.Controls.Add(BuildTopBar(), 0, 1);

        this.mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = Canvas,
            Panel1MinSize = 360,
            Panel2MinSize = 360
        };
        this.mainSplit.Panel1.Padding = new Padding(14);
        this.mainSplit.Panel2.Padding = new Padding(14);
        this.mainSplit.Panel1.Controls.Add(BuildCustomerArea());
        this.mainSplit.Panel2.Controls.Add(BuildOrderArea());
        root.Controls.Add(this.mainSplit, 0, 2);

        root.Controls.Add(BuildStatusBar(), 0, 3);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Surface, ForeColor = Navy, Padding = new Padding(6, 4, 0, 4) };
        var administration = new ToolStripMenuItem("Administration");
        var priceMaintenance = new ToolStripMenuItem("Price Maintenance...");
        priceMaintenance.Click += async (_, _) => await OpenProductMaintenanceAsync();
        administration.DropDownItems.Add(priceMaintenance);
        administration.DropDownItems.Add(new ToolStripSeparator());
        var diagnostics = new ToolStripMenuItem("Diagnostics");
        diagnostics.Click += (_, _) => OpenEngineeringDiagnostics();
        administration.DropDownItems.Add(diagnostics);
        var reports = new ToolStripMenuItem("Reports");
        PopulateReportsMenu(reports);
        menu.Items.Add(administration);
        menu.Items.Add(reports);
        return menu;
    }

    private Control BuildTopBar()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Navy, Padding = new Padding(14, 12, 14, 10) };
        var title = new Label { Text = "CAFETERIA", AutoSize = true, ForeColor = Color.White, Font = new Font("Segoe UI", 17, FontStyle.Bold) };
        panel.Controls.Add(title);

        var searchPanel = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        this.lookup.Height = 36;
        searchPanel.Controls.Add(this.lookup);
        searchPanel.Controls.Add(this.find);
        searchPanel.Controls.Add(this.cashCustomer);
        searchPanel.Location = new Point(panel.Width - searchPanel.Width - 14, 14);
        searchPanel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        panel.Controls.Add(searchPanel);
        return panel;
    }

    private Control BuildCustomerArea()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        host.Controls.Add(this.customerPanel);
        host.Controls.Add(this.idlePanel);
        this.idlePanel.BringToFront();

        BuildIdleDashboard();
        BuildCustomerPanel();
        return host;
    }

    private void BuildIdleDashboard()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            Text = "TODAY",
            AutoSize = true,
            ForeColor = Navy,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1 };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        cards.Controls.Add(DashboardCard("NET SALES", this.todayNetSales), 0, 0);
        cards.Controls.Add(DashboardCard("TRANSACTIONS", this.todayTransactions), 1, 0);
        cards.Controls.Add(DashboardCard("REFUNDS", this.todayRefunds), 2, 0);
        root.Controls.Add(cards, 0, 1);

        var charts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 14, 0, 0) };
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        charts.Controls.Add(DashboardChartCard("ITEMS SOLD", this.todayItemsByQuantity), 0, 0);
        charts.Controls.Add(DashboardChartCard("SALES VALUE", this.todayItemsByValue), 1, 0);
        root.Controls.Add(charts, 0, 2);
        root.Controls.Add(this.todayDashboardStatus, 0, 3);
        this.idlePanel.Controls.Add(root);
    }

    private void BuildCustomerPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var identity = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1 };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identity.Controls.Add(this.photo, 0, 0);
        var details = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Padding = new Padding(14, 0, 0, 0) };
        details.Controls.Add(this.customerName);
        details.Controls.Add(this.eligibility);
        details.Controls.Add(this.period);
        details.Controls.Add(this.fundingStatus);
        identity.Controls.Add(details, 1, 0);
        root.Controls.Add(identity, 0, 0);

        var money = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 0, Margin = new Padding(0, 14, 0, 14) };
        money.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        money.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        AddFinancialRow(money, "Monthly Credit Limit", this.creditLimit);
        AddFinancialRow(money, "Company Subsidy Remaining", this.subsidyRemaining);
        AddFinancialRow(money, "Employee-funded Capacity", this.nonSubsidyRemaining);
        AddFinancialRow(money, "Available to Spend", this.available);
        AddFinancialRow(money, "Carried Debt", this.carriedDebt);
        AddFinancialRow(money, "Customer Responsibility", this.customerLiability);
        AddFinancialRow(money, "Customer Payments", this.customerPaymentsValue);
        AddFinancialRow(money, "Employee Amount Due", this.employeeAmountDue);
        root.Controls.Add(money, 0, 1);
        root.Controls.Add(this.transactions, 0, 2);
        this.customerPanel.Controls.Add(root);
    }

    private Control BuildOrderArea()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 24));

        var productsPanel = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        productsPanel.Controls.Add(this.productGrid);
        productsPanel.Controls.Add(this.productSearch);
        root.Controls.Add(productsPanel, 0, 0);

        var cartPanel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(0, 12, 0, 8) };
        cartPanel.Controls.Add(this.cartGrid);
        root.Controls.Add(cartPanel, 0, 1);

        this.transactionWorkspace = BuildCheckoutPanel();
        this.recentTransactionsPanel = BuildRecentTransactionsPanel();

        var workspace = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        workspace.Controls.Add(this.transactionWorkspace);
        workspace.Controls.Add(this.recentTransactionsPanel);
        this.recentTransactionsPanel.Visible = false;
        root.Controls.Add(workspace, 0, 2);
        return root;
    }

    private Control BuildCheckoutPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var total = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        total.Controls.Add(this.cartCaption);
        total.Controls.Add(this.cartTotal);
        root.Controls.Add(total, 0, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        actions.Controls.Add(this.checkoutButton);
        actions.Controls.Add(this.voidButton);
        actions.Controls.Add(this.clearButton);
        actions.Controls.Add(this.customerPaymentButton);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(this.voidAvailability, 0, 2);
        return root;
    }

    private Panel BuildRecentTransactionsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(0, 4, 0, 0) };
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        header.Controls.Add(new Label { Text = "RECENT TRANSACTIONS", AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold), ForeColor = Navy, Margin = new Padding(0, 8, 16, 0) });
        header.Controls.Add(this.refundButton);
        panel.Controls.Add(this.recentTransactionGrid);
        panel.Controls.Add(header);
        return panel;
    }

    private StatusStrip BuildStatusBar()
    {
        var strip = new StatusStrip { SizingGrip = false, BackColor = Surface };
        strip.Items.Add(new ToolStripControlHost(this.posStatus));
        strip.Items.Add(new ToolStripControlHost(this.operatorStatus));
        strip.Items.Add(new ToolStripControlHost(this.syncStatus));
        strip.Items.Add(new ToolStripControlHost(this.cacheStatus));
        return strip;
    }

    private static Control DashboardCard(string title, Label value)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 92, BackColor = Color.FromArgb(247, 249, 251), Padding = new Padding(12), Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(value);
        value.Location = new Point(12, 38);
        panel.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), Location = new Point(12, 12) });
        return panel;
    }

    private static Control DashboardChartCard(string title, Control chart)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 251), Padding = new Padding(12), Margin = new Padding(0, 0, 10, 0) };
        panel.Controls.Add(chart);
        chart.Dock = DockStyle.Fill;
        panel.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), Dock = DockStyle.Top, Height = 28 });
        return panel;
    }

    private void ApplyMainSplitLayout()
    {
        if (this.mainSplit is null) return;
        int usable = Math.Max(0, this.mainSplit.ClientSize.Width - this.mainSplit.SplitterWidth);
        if (usable <= 0) return;
        this.mainSplit.SplitterDistance = usable / 2;
    }

    private void ConfigureGrids()
    {
        this.productGrid.DataSource = this.productSource;
        this.cartGrid.DataSource = this.cartSource;
        this.transactions.DataSource = this.transactionSource;
        this.recentTransactionGrid.DataSource = this.recentTransactionSource;

        this.productGrid.Columns.Clear();
        this.productGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Product", FillWeight = 60 });
        this.productGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Price", HeaderText = "Price", FillWeight = 20, DefaultCellStyle = { Format = "C2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        this.productGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Code", HeaderText = "Code", FillWeight = 20 });

        this.cartGrid.Columns.Clear();
        this.cartGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ProductName", HeaderText = "Item", FillWeight = 52 });
        this.cartGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "UnitPrice", HeaderText = "Price", FillWeight = 16, DefaultCellStyle = { Format = "C2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        this.cartGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Quantity", HeaderText = "Qty", FillWeight = 12, DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
        this.cartGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Total", HeaderText = "Total", FillWeight = 20, DefaultCellStyle = { Format = "C2", Alignment = DataGridViewContentAlignment.MiddleRight } });

        this.transactions.Columns.Clear();
        this.transactions.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OccurredAt", HeaderText = "Date", FillWeight = 24, DefaultCellStyle = { Format = "g" } });
        this.transactions.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Kind", HeaderText = "Type", FillWeight = 18 });
        this.transactions.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Description", HeaderText = "Description", FillWeight = 40 });
        this.transactions.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Amount", HeaderText = "Amount", FillWeight = 18, DefaultCellStyle = { Format = "C2", Alignment = DataGridViewContentAlignment.MiddleRight } });

        this.recentTransactionGrid.Columns.Clear();
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OccurredAt", HeaderText = "Date", FillWeight = 22, DefaultCellStyle = { Format = "g" } });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "CustomerName", HeaderText = "Customer", FillWeight = 28 });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Kind", HeaderText = "Type", FillWeight = 16 });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Description", HeaderText = "Description", FillWeight = 22 });
        this.recentTransactionGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Amount", HeaderText = "Amount", FillWeight = 12, DefaultCellStyle = { Format = "C2", Alignment = DataGridViewContentAlignment.MiddleRight } });
    }

    private async Task LoadProductsAsync(string? search = null)
    {
        try
        {
            this.productSource.DataSource = await this.products.SearchAsync(search ?? string.Empty);
            this.syncStatus.Text = "● Product list ready";
            this.syncStatus.ForeColor = Green;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Could not load Cafeteria products");
            this.productSource.DataSource = Array.Empty<ProductCatalogItem>();
            this.syncStatus.Text = "● Product list unavailable";
            this.syncStatus.ForeColor = Red;
        }
    }

    private async Task LoadRecentTransactionsAsync()
    {
        try
        {
            var rows = await this.recentTransactions.GetTodayAsync(this.persistenceOptions.TerminalId, this.uiOptions.RecentTransactionCount);
            this.recentTransactionSource.DataSource = rows;
            UpdateRecentTransactionsVisibility();
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not load today's recent POS transactions");
            this.recentTransactionSource.DataSource = Array.Empty<RecentTransactionRow>();
            UpdateRecentTransactionsVisibility();
        }
    }

    private async Task LoadIdleDashboardAsync()
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var reportSource = this.services.GetRequiredService<IReportSource>();
            var report = await reportSource.BuildAsync(ReportKind.DailyDashboard, today, today);
            var productReport = await reportSource.BuildAsync(ReportKind.ProductSales, today, today);
            var summary = report.Rows.FirstOrDefault();
            this.todayNetSales.Text = summary is null ? "$0.00" : Money(summary.Total);
            this.todayTransactions.Text = summary is null ? "0" : summary.Count.ToString("N0");
            this.todayRefunds.Text = summary is null ? "$0.00" : Money(summary.Refunds);
            this.todayItemsByQuantity.SetSlices
            (
                productReport.Rows
                    .Where(row => row.Quantity > 0)
                    .Select(row => new PieChartSlice(row.Label, row.Quantity))
                    .ToArray()
            );
            this.todayItemsByValue.SetSlices
            (
                productReport.Rows
                    .Where(row => row.Total > 0m)
                    .Select(row => new PieChartSlice(row.Label, row.Total))
                    .ToArray()
            );
            this.todayDashboardStatus.Text = $"Updated {DateTime.Now:t}";
        }
        catch (Exception ex)
        {
            this.logger.LogWarning(ex, "Could not load POS daily dashboard");
            this.todayNetSales.Text = "$0.00";
            this.todayTransactions.Text = "0";
            this.todayRefunds.Text = "$0.00";
            this.todayItemsByQuantity.SetSlices([]);
            this.todayItemsByValue.SetSlices([]);
            this.todayDashboardStatus.Text = "Dashboard data unavailable.";
        }
    }

    private async Task IdentifyAsync()
    {
        RegisterActivity();
        var term = this.lookup.Text.Trim();
        if (string.IsNullOrWhiteSpace(term))
        {
            var browse = await this.identification.SearchAsync(string.Empty, 50);
            if (browse.Count == 0)
            {
                MessageBox.Show(this, "No eligible Cafeteria employees are available to browse.", "Find Customer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                FocusCustomerLookup();
                return;
            }

            using var dialog = new CustomerSelectionDialog(browse);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCustomerId is int customerId)
            {
                var selected = await this.identification.LoadAsync(customerId);
                if (selected is not null) ShowCustomer(selected);
            }
            FocusCustomerLookup();
            return;
        }
        this.posStatus.Text = "● Searching...";
        try
        {
            var result = await this.identification.IdentifyAsync(term);
            if (result.Status == CustomerIdentificationStatus.Found && result.Customer is not null)
            {
                ShowCustomer(result.Customer);
                return;
            }
            if (result.Status == CustomerIdentificationStatus.MultipleMatches)
            {
                using var dialog = new CustomerSelectionDialog(result.Candidates);
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCustomerId is int id)
                {
                    var selected = await this.identification.LoadAsync(id);
                    if (selected is not null) ShowCustomer(selected);
                }
                FocusCustomerLookup();
                return;
            }
            this.posStatus.Text = "● Customer not found";
            this.posStatus.ForeColor = Red;
            FocusCustomerLookup();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Customer lookup failed for {SearchTerm}", term);
            this.posStatus.Text = "● Customer lookup unavailable";
            this.posStatus.ForeColor = Red;
            MessageBox.Show(this, ex.GetBaseException().Message, "Find Customer", MessageBoxButtons.OK, MessageBoxIcon.Error);
            FocusCustomerLookup();
        }
    }

    private void ShowCustomer(CustomerAccountView customer)
    {
        this.currentCustomer = customer;
        RegisterActivity();
        this.idlePanel.Visible = false;
        this.customerPanel.Visible = true;
        this.customerPanel.BringToFront();
        UpdateRecentTransactionsVisibility();
        this.customerName.Text = customer.Customer.Name;
        this.eligibility.Text = customer.Customer.IsCashCustomer
            ? "CASH CUSTOMER"
            : customer.Customer.IsEligibleForCredit ? "ELIGIBLE" : "NOT ELIGIBLE";
        this.eligibility.BackColor = customer.Customer.IsCashCustomer ? Color.FromArgb(231, 237, 242) : customer.Customer.IsEligibleForCredit ? Color.FromArgb(221, 242, 231) : Color.FromArgb(252, 225, 225);
        this.eligibility.ForeColor = customer.Customer.IsCashCustomer ? Navy : customer.Customer.IsEligibleForCredit ? Green : Red;
        this.period.Text = customer.Customer.IsCashCustomer ? "No employee account" : $"Period: {customer.Period.Year}-{customer.Period.Month:00}";
        SetFundingStatus(customer);
        this.creditLimit.Text = Money(customer.FinancialStatus.MonthlyCreditLimit);
        this.subsidyRemaining.Text = Money(customer.FinancialStatus.SubsidyAmountRemaining);
        this.nonSubsidyRemaining.Text = Money(customer.FinancialStatus.EmployeeFundedCapacityRemaining);
        this.available.Text = Money(customer.FinancialStatus.TotalAvailableToSpend);
        this.carriedDebt.Text = Money(customer.FinancialStatus.CarriedDebt);
        this.customerLiability.Text = Money(customer.FinancialStatus.CustomerResponsibility);
        this.customerPaymentsValue.Text = Money(customer.FinancialStatus.CustomerPayments);
        this.employeeAmountDue.Text = Money(customer.FinancialStatus.EmployeeAmountDue);
        this.transactionSource.DataSource = customer.Transactions.OrderByDescending(x => x.OccurredAt).ToArray();
        this.customerPaymentButton.Enabled = !customer.Customer.IsCashCustomer && customer.Customer.IsEligibleForCredit && !customer.Period.IsClosed && customer.FinancialStatus.EmployeeAmountDue > 0m;
        SetPhoto(customer.Customer.Photo);
        this.customerDisplay?.ShowCustomer(customer);
        this.posStatus.Text = $"● Customer: {customer.Customer.Name}";
        this.posStatus.ForeColor = Green;
        UpdateCheckoutState();
        this.productSearch.Focus();
    }

    private void SetFundingStatus(CustomerAccountView customer)
    {
        if (customer.Customer.IsCashCustomer)
        {
            this.fundingStatus.Text = "Cash-only sale • No Cafeteria Account or company subsidy";
            this.fundingStatus.ForeColor = Muted;
            return;
        }
        if (!customer.Customer.IsEligibleForCredit)
        {
            this.fundingStatus.Text = "Not eligible for Cafeteria Account purchases";
            this.fundingStatus.ForeColor = Red;
            return;
        }
        if (customer.Period.IsClosed)
        {
            this.fundingStatus.Text = "Period closed • New Cafeteria Account charges are blocked";
            this.fundingStatus.ForeColor = Red;
            return;
        }
        this.fundingStatus.Text = customer.FinancialStatus.EmployeeAmountDue > 0m
            ? $"Employee Amount Due: {Money(customer.FinancialStatus.EmployeeAmountDue)} • Cash/customer payment can reduce the balance"
            : "Cafeteria Account available • Company subsidy applies only to Cafeteria Account charges";
        this.fundingStatus.ForeColor = customer.FinancialStatus.EmployeeAmountDue > 0m ? Color.DarkOrange : Green;
    }

    private void AddSelectedProduct(int rowIndex)
    {
        RegisterActivity();
        if (rowIndex < 0 || this.productSource[rowIndex] is not ProductCatalogItem item) return;
        var existing = this.cart.FirstOrDefault(x => x.ProductId == item.Id);
        if (existing is null)
            this.cart.Add(new CartRow(item.Id, item.Name, item.Price, 1));
        else
            existing.Quantity++;
        RefreshCart();
    }

    private void RemoveCartRow(int rowIndex)
    {
        RegisterActivity();
        if (rowIndex < 0 || rowIndex >= this.cart.Count) return;
        this.cart.RemoveAt(rowIndex);
        RefreshCart();
    }

    private void RefreshCart()
    {
        this.cartSource.DataSource = this.cart.ToArray();
        var total = this.cart.Sum(x => x.Total);
        this.cartTotal.Text = Money(total);
        UpdatePaymentState(total);
        UpdateCheckoutState();
    }

    private async Task CheckoutAsync()
    {
        if (this.currentCustomer is null || this.cart.Count == 0) return;
        var total = this.cart.Sum(x => x.Total);
        using var dialog = new PaymentDialog(this.currentCustomer, total);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            this.checkoutButton.Enabled = false;
            var operatorId = this.operatorSession.Current?.UserId ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required.");
            var result = await this.checkout.CompleteAsync(
                this.currentCustomer,
                this.cart.Select(x => new PosOrderLine(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity)).ToArray(),
                dialog.CafeteriaAccountAmount,
                dialog.CashReceived,
                operatorId,
                this.persistenceOptions.TerminalId);
            this.posStatus.Text = $"● Sale completed • Change {Money(result.ChangeDue)}";
            this.posStatus.ForeColor = Green;
            this.cart.Clear();
            RefreshCart();
            var refreshed = await this.identification.LoadAsync(this.currentCustomer.Customer.Id);
            if (refreshed is not null) ShowCustomer(refreshed);
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Checkout failed for customer {CustomerId}", this.currentCustomer.Customer.Id);
            MessageBox.Show(this, ex.GetBaseException().Message, "Complete Sale", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateCheckoutState();
        }
    }

    private void UpdatePaymentState(decimal? totalOverride = null)
    {
        decimal total = totalOverride ?? this.cart.Sum(item => item.Total);
        decimal accountAmount = 0m;
        decimal subsidy = 0m;
        if (this.currentCustomer?.Customer.IsEligibleForCredit == true && !this.currentCustomer.Period.IsClosed)
        {
            accountAmount = Math.Min(total, this.currentCustomer.FinancialStatus.TotalAvailableToSpend);
            subsidy = this.currentCustomer.FinancialStatus.SubsidyAmountRemaining;
        }
        var employeeResponsibility = Math.Max(0m, accountAmount - subsidy);
        this.voidAvailability.Text = total <= 0m
            ? "Add an item to begin the sale."
            : $"Account {Money(accountAmount)} • New Customer Responsibility {Money(employeeResponsibility)}";
        this.voidAvailability.ForeColor = employeeResponsibility > 0m ? Color.DarkOrange : Muted;
    }

    private async Task VoidCurrentSaleAsync()
    {
        if (this.currentCustomer is null || this.cart.Count == 0) return;
        var manager = await AuthorizeManagerAsync("Void current sale");
        if (manager is null) return;
        using var reasonDialog = new ReasonDialog("Void Sale", "Reason for voiding this sale:");
        if (reasonDialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(reasonDialog.Reason)) return;
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
            this.posStatus.ForeColor = Red;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Void failed for operator {ManagerUserId}", manager.UserId);
            MessageBox.Show(this, ex.GetBaseException().Message, "Void Sale", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
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

    private async Task RefundSelectedAsync()
    {
        if (this.currentCustomer is null) return;

        if (this.transactions.CurrentRow?.DataBoundItem is TransactionRow selected &&
            selected.Kind.Equals("Refund", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show
            (
                  this
                , "Refund entries cannot be refunded. Select the original purchase instead."
                , "Refund"
                , MessageBoxButtons.OK
                , MessageBoxIcon.Information
            );
            return;
        }

        var order = await SelectRefundOrderAsync();
        if (order is null) return;
        var refunded = await this.refundService.GetRefundedQuantitiesAsync(order.Id);
        using var dialog = new RefundDialog(order, refunded);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Requests.Count == 0) return;
        try
        {
            var operatorId = this.operatorSession.Current?.UserId ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required.");
            var result = await this.refundService.RefundAsync(order, dialog.Requests, operatorId, dialog.Reason);
            this.posStatus.Text = $"● Refunded {Money(result.RefundedAmount)}";
            this.posStatus.ForeColor = Red;
            var refreshed = await this.identification.LoadAsync(this.currentCustomer.Customer.Id);
            if (refreshed is not null) ShowCustomer(refreshed);
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Refund failed for order {OrderId}", order.Id);
            MessageBox.Show(this, ex.GetBaseException().Message, "Refund", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RecordCustomerPaymentAsync()
    {
        if (this.currentCustomer is null || this.currentCustomer.Customer.IsCashCustomer) return;
        if (this.currentCustomer.Period.IsClosed)
        {
            MessageBox.Show(this, "Customer payments are blocked because this account period is closed.", "Customer Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var due = this.currentCustomer.FinancialStatus.EmployeeAmountDue;
        if (due <= 0m)
        {
            MessageBox.Show(this, "This customer has no outstanding cafeteria responsibility to pay.", "Customer Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new CustomerPaymentDialog(this.currentCustomer.Customer.Name, due);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            this.customerPaymentButton.Enabled = false;
            var operatorId = this.operatorSession.Current?.UserId ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required.");
            await this.customerPayments.RecordAsync(this.currentCustomer, dialog.PaymentAmount, dialog.PaymentMethod, operatorId, dialog.Reference);
            var customerId = this.currentCustomer.Customer.Id;
            var refreshed = await this.identification.LoadAsync(customerId);
            if (refreshed is not null) ShowCustomer(refreshed);
            this.posStatus.Text = $"● Customer payment recorded: {Money(dialog.PaymentAmount)}";
            this.posStatus.ForeColor = Green;
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Customer payment failed for customer {CustomerId}", this.currentCustomer.Customer.Id);
            MessageBox.Show(this, ex.GetBaseException().Message, "Customer Payment", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (this.currentCustomer is not null)
                this.customerPaymentButton.Enabled = !this.currentCustomer.Customer.IsCashCustomer && this.currentCustomer.Customer.IsEligibleForCredit && !this.currentCustomer.Period.IsClosed && this.currentCustomer.FinancialStatus.EmployeeAmountDue > 0m;
        }
    }

    private async Task<Order?> SelectRefundOrderAsync()
    {
        if (this.currentCustomer is null) return null;
        if (this.transactions.CurrentRow?.DataBoundItem is TransactionRow selected &&
            selected.Kind.Equals("Purchase", StringComparison.OrdinalIgnoreCase))
        {
            var matching = await this.orderQuery.GetRecentCompletedAsync(this.currentCustomer.Customer.Id, 50);
            var exact = matching.FirstOrDefault(x => x.Id == selected.ReferenceId && x.RefundedAmount < x.Total);
            if (exact is not null) return exact;
        }

        // Search enough recent completed orders to find up to five that still have refundable value.
        // Keeping the chooser to five rows makes the refund workflow easier to scan at the POS.
        var recent = await this.orderQuery.GetRecentCompletedAsync(this.currentCustomer.Customer.Id, 50);
        var refundable = recent
            .Where(x => x.RefundedAmount < x.Total)
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

        // An uncompleted POS transaction exists only in the current UI/cart state.
        // Escape abandons it without creating an Order, void, refund, or FFP ledger event.
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

        // Idle: show today's recent terminal activity. Active customer/sale: restore
        // the original transaction workspace wholesale, with its original proportions.
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

    private static string Money(decimal value) => value.ToString("C2");

    private static Button ActionButton(string text, Color backColor, int width, int height = 36) => new WhiteTextActionButton
    {
        Text = text,
        Width = width,
        Height = height,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        UseVisualStyleBackColor = false,
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
