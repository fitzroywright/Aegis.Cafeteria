using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Export;
using Aegis.Cafeteria.Reporting.Models;
using System.Diagnostics;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Aegis.Cafeteria.UI.Linux;

public sealed class MainWindow : Window
{
    private readonly ICustomerIdentificationService identification;
    private readonly IProductCatalogQuery products;
    private readonly PosCheckoutService checkout;
    private readonly ILogger<MainWindow> logger;
    private readonly ICustomerReferenceCacheStatus employeeCacheStatusProvider;
    private readonly CafeteriaHealthOptions healthOptions;
    private readonly CafeteriaUiOptions uiOptions;
    private readonly IRecentTransactionQuery recentTransactionQuery;
    private readonly CafeteriaPersistenceOptions persistenceOptions;
    private readonly IOperatorSession operatorSession;
    private readonly IOperatorAuthenticator operatorAuthenticator;
    private readonly IManagerAuthorizationRegistry managerAuthorization;
    private readonly PosVoidService voidService;
    private readonly RefundService refundService;
    private readonly CustomerPaymentWorkflow customerPayments;
    private readonly IOrderQuery orderQuery;
    private readonly ICafeteriaPersistenceStore persistenceStore;
    private readonly IServiceProvider services;
    private readonly IDisposable accountChangeSubscription;
    private readonly DispatcherTimer cacheTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly DispatcherTimer inactivityTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer nameSearchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTimeOffset lastActivityUtc = DateTimeOffset.UtcNow;
    private CancellationTokenSource? nameSearchCancellation;
    private bool closeConfirmed;
    private Border? recentTransactionsPanel;
    private RowDefinition? recentTransactionsRow;
    private CustomerDisplayWindow? customerDisplay;
    private Avalonia.Platform.Screen? customerDisplayScreen;
    private Control? orderWorkspace;
    private Control? idleDashboard;

    private readonly TextBox lookup = new() { Watermark = "Tap card or enter Code, TRN, or Name", MinWidth = 420 };
    private readonly Button findButton = new() { Content = "Find", MinWidth = 80 };
    private readonly Button cashCustomerButton = new() { Content = "Cash Customer", MinWidth = 120 };
    private readonly TextBlock status = new() { Text = "Ready", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock cacheStatus = new() { Text = "Employee cache: checking...", VerticalAlignment = VerticalAlignment.Center };
    private readonly Button refreshEmployeesButton = new() { Content = "Refresh reference data", MinWidth = 130 };

    private readonly Image photo = new() { Width = 140, Height = 140, Stretch = Avalonia.Media.Stretch.Uniform };
    private readonly TextBlock customerName = new() { Text = "No customer selected", FontSize = 22, FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock period = new();
    private readonly TextBlock fundingStatus = new() { FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock creditLimit = ValueText();
    private readonly TextBlock subsidyRemaining = ValueText();
    private readonly TextBlock nonSubsidyRemaining = ValueText();
    private readonly TextBlock available = ValueText(true);
    private readonly TextBlock carriedDebt = ValueText();
    private readonly TextBlock customerLiability = ValueText();
    private readonly TextBlock customerPaymentsValue = ValueText();
    private readonly TextBlock employeeAmountDue = ValueText();

    private readonly ListBox searchResults = new() { MinHeight = 80, MaxHeight = 130 };
    private readonly ListBox transactions = new();
    private readonly ListBox recentTransactions = new() { MinHeight = 120, MaxHeight = 180 };
    private readonly TextBox productSearch = new() { Watermark = "Search products" };
    private readonly ListBox productList = new();
    private readonly ListBox cartList = new();
    private readonly TextBlock cartTotal = new() { Text = "Total: $0.00", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBox cafeteriaTender = new() { Watermark = "Cafeteria account", Text = "0.00" };
    private readonly TextBox cashTender = new() { Watermark = "Cash received", Text = "0.00" };
    private readonly TextBlock paymentRemaining = new() { Text = "Remaining: $0.00", FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly TextBlock changeDue = new() { Text = "Change: $0.00", FontWeight = Avalonia.Media.FontWeight.Bold };
    private bool updatingTender;
    private readonly TextBlock newResponsibility = new() { Text = "New Customer Responsibility: $0.00", FontWeight = Avalonia.Media.FontWeight.Bold };
    private readonly Button checkoutButton = new() { Content = "Complete Sale", IsEnabled = false, MinHeight = 42 };
    private readonly Button clearButton = new() { Content = "New Customer", MinHeight = 42, MinWidth = 130 };
    private readonly Button voidButton = new() { Content = "Void Current Sale", MinHeight = 42, MinWidth = 150, IsEnabled = false };
    private readonly Button refundButton = new() { Content = "Refund Selected", MinHeight = 32, MinWidth = 130, IsEnabled = false };
    private readonly Button customerPaymentButton = new() { Content = "Customer Payment...", MinHeight = 38, MinWidth = 155, IsEnabled = false };
    private readonly TextBlock voidAvailability = new() { FontWeight = Avalonia.Media.FontWeight.Bold, FontSize = 12 };
    private readonly TextBlock operatorStatus = new() { Text = "Operator: not signed in", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock todayNetSales = DashboardValue();
    private readonly TextBlock todayTransactions = DashboardValue();
    private readonly TextBlock todayRefunds = DashboardValue();
    private readonly TextBlock todayDashboardStatus = new();
    private readonly WeekdayBarChartView weeklySalesByDay = new();
    private readonly PieChartView todayItemsByValue = new();

    private CustomerAccountView? currentCustomer;
    private IReadOnlyList<ProductCatalogItem> currentProducts = Array.Empty<ProductCatalogItem>();
    private IReadOnlyList<CustomerSearchCandidate> currentCandidates = Array.Empty<CustomerSearchCandidate>();
    private readonly List<CartRow> cart = [];

    public MainWindow(
        ICustomerIdentificationService identification,
        IProductCatalogQuery products,
        PosCheckoutService checkout,
        ICustomerReferenceCacheStatus employeeCacheStatusProvider,
        CafeteriaHealthOptions healthOptions,
        CafeteriaUiOptions uiOptions,
        IRecentTransactionQuery recentTransactionQuery,
        CafeteriaPersistenceOptions persistenceOptions,
        IOperatorSession operatorSession,
        IOperatorAuthenticator operatorAuthenticator,
        IManagerAuthorizationRegistry managerAuthorization,
        PosVoidService voidService,
        RefundService refundService,
        CustomerPaymentWorkflow customerPayments,
        IOrderQuery orderQuery,
        ICafeteriaPersistenceStore persistenceStore,
        ICustomerAccountChangeNotifier accountChangeNotifier,
        IServiceProvider services,
        ILogger<MainWindow> logger)
    {
        this.identification = identification;
        this.products = products;
        this.checkout = checkout;
        this.employeeCacheStatusProvider = employeeCacheStatusProvider;
        this.healthOptions = healthOptions;
        this.uiOptions = uiOptions;
        this.recentTransactionQuery = recentTransactionQuery;
        this.persistenceOptions = persistenceOptions;
        this.operatorSession = operatorSession;
        this.operatorAuthenticator = operatorAuthenticator;
        this.managerAuthorization = managerAuthorization;
        this.voidService = voidService;
        this.refundService = refundService;
        this.customerPayments = customerPayments;
        this.orderQuery = orderQuery;
        this.persistenceStore = persistenceStore;
        this.accountChangeSubscription = accountChangeNotifier.Subscribe(OnCustomerAccountChangedAsync);
        this.services = services;
        this.logger = logger;

        Title = "Cafeteria";
        Background = new SolidColorBrush(Color.Parse("#F4F7FA"));
        Width = 1360;
        Height = 850;
        MinWidth = 1100;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowState = WindowState.Maximized;

        Content = BuildLayout();
        ApplyTheme();

        Opened += async (_, _) =>
        {
            var login = new OperatorLoginWindow(operatorAuthenticator, operatorSession);
            var signedIn = await login.ShowDialog<bool>(this);
            if (!signedIn) { closeConfirmed = true; Close(); return; }
            UpdateOperatorStatus();
            InitializeCustomerDisplay();
            await LoadProductsAsync();
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
            await UpdateCacheStatusAsync();
            cacheTimer.Start();
            if (uiOptions.CustomerInactivityTimeoutSeconds > 0)
            {
                RegisterActivity();
                inactivityTimer.Start();
            }
            FocusCustomerLookup();
        };
        Closed += (_, _) =>
        {
            cacheTimer.Stop();
            inactivityTimer.Stop();
            nameSearchTimer.Stop();
            nameSearchCancellation?.Cancel();
            nameSearchCancellation?.Dispose();
            accountChangeSubscription.Dispose();
            customerDisplay?.Close();
            customerDisplay = null;
        };
        Closing += async (_, e) =>
        {
            if (closeConfirmed) return;
            e.Cancel = true;
            var confirmed = await new ConfirmWindow("Confirm Close", "Are you sure you want to close Cafeteria?").ShowDialog<bool>(this);
            if (!confirmed) return;
            closeConfirmed = true;
            Close();
        };
        findButton.Click += async (_, _) => await IdentifyAsync();
        cashCustomerButton.Click += (_, _) => ShowCustomer(CashCustomerAccountViewFactory.Create(DateOnly.FromDateTime(DateTime.Today)));
        lookup.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await IdentifyAsync();
        };
        lookup.TextChanged += (_, _) =>
        {
            RegisterActivity();
            nameSearchTimer.Stop();
            if (CustomerLookupResolver.LooksLikeNameSearch(lookup.Text)) nameSearchTimer.Start();
        };
        nameSearchTimer.Tick += async (_, _) =>
        {
            nameSearchTimer.Stop();
            await SearchNamesAsync();
        };
        inactivityTimer.Tick += (_, _) => CheckCustomerInactivity();
        PointerMoved += (_, _) => RegisterActivity();
        PointerPressed += (_, _) => RegisterActivity();
        KeyDown += (_, e) =>
        {
            RegisterActivity();
            if (e.Key != Key.Escape || currentCustomer is null) return;
            e.Handled = true;
            CancelActiveTransaction();
        };
        productSearch.TextChanged += async (_, _) => { RegisterActivity(); await LoadProductsAsync(productSearch.Text); };
        productList.DoubleTapped += (_, _) => AddSelectedProduct();
        cartList.DoubleTapped += (_, _) => RemoveSelectedCartRow();
        searchResults.DoubleTapped += async (_, _) => await LoadSelectedCandidateAsync();
        cafeteriaTender.TextChanged += (_, _) => UpdatePaymentState();
        cashTender.TextChanged += (_, _) => UpdateAccountTenderFromCash();
        checkoutButton.Click += async (_, _) => { RegisterActivity(); await CheckoutAsync(); };
        voidButton.Click += async (_, _) => await VoidCurrentSaleAsync();
        refundButton.Click += async (_, _) => await RefundSelectedAsync();
        customerPaymentButton.Click += async (_, _) => await RecordCustomerPaymentAsync();
        clearButton.Click += (_, _) => ClearCustomer();
        refreshEmployeesButton.Click += async (_, _) => await RefreshCustomersAsync();
        cacheTimer.Tick += async (_, _) => { await UpdateCacheStatusAsync(); await LoadRecentTransactionsAsync(); await LoadIdleDashboardAsync(); };
    }

    private Control BuildLayout()
    {
        var root = new Grid { RowSpacing = 0 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        recentTransactionsRow = new RowDefinition(uiOptions.RecentTransactionCount > 0 ? new GridLength(165) : new GridLength(0));
        root.RowDefinitions.Add(recentTransactionsRow);
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(BuildMenu());
        var header = BuildHeader();
        Grid.SetRow(header, 1);
        root.Children.Add(header);

        var body = new Grid { ColumnSpacing = 14, Margin = new Thickness(18, 14, 18, 10) };
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var left = new Border { Background = Brushes.White, Padding = new Thickness(18), CornerRadius = new CornerRadius(6), Child = BuildCustomerPanel() };
        body.Children.Add(left);
        orderWorkspace = BuildOrderPanel();
        idleDashboard = BuildIdleDashboard();
        orderWorkspace.IsVisible = false;
        idleDashboard.IsVisible = true;
        var orderHost = new Grid();
        orderHost.Children.Add(orderWorkspace);
        orderHost.Children.Add(idleDashboard);
        var right = new Border { Background = Brushes.White, Padding = new Thickness(18), CornerRadius = new CornerRadius(6), Child = orderHost };
        Grid.SetColumn(right, 1);
        body.Children.Add(right);

        var recent = BuildRecentTransactionsPanel();
        Grid.SetRow(recent, 3);
        root.Children.Add(recent);

        var statusBar = new Border { Background = new SolidColorBrush(Color.FromRgb(234, 238, 242)), Padding = new Thickness(18, 8) };
        var statusItems = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        statusItems.Children.Add(operatorStatus);
        statusItems.Children.Add(status);
        statusItems.Children.Add(cacheStatus);
        statusItems.Children.Add(refreshEmployeesButton);
        statusBar.Child = statusItems;
        Grid.SetRow(statusBar, 4);
        root.Children.Add(statusBar);
        return root;
    }

    private Control BuildMenu()
    {
        var menu = new Menu { Background = Brushes.White };
        var reports = new MenuItem
        {
            Header = "Reports",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(24, 45, 68))
        };

        var intro = new MenuItem
        {
            Header = "Report Center...   Preview • Excel • PDF",
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 111, 190))
        };
        intro.Click += async (_, _) => await OpenReportCenterAsync(CafeteriaReportKind.ExecutiveSummary);
        reports.Items.Add(intro);
        reports.Items.Add(new Separator());

        foreach (var group in new[]
        {
            new { Name = "Overview & KPIs", Kinds = new[] { CafeteriaReportKind.ExecutiveSummary } },
            new { Name = "Sales & Purchases", Kinds = new[] { CafeteriaReportKind.DailySales, CafeteriaReportKind.SalesTrend, CafeteriaReportKind.TenderMix, CafeteriaReportKind.ProductSales, CafeteriaReportKind.PurchaseItemDetail, CafeteriaReportKind.PurchaseByPerson } },
            new { Name = "Employee & Credit", Kinds = new[] { CafeteriaReportKind.CanteenSummary, CafeteriaReportKind.EmployeeStatement, CafeteriaReportKind.MonthlyAllocations, CafeteriaReportKind.SubsidyUtilization, CafeteriaReportKind.CustomerResponsibility, CafeteriaReportKind.OutstandingDebt } },
            new { Name = "Audit & Reconciliation", Kinds = new[] { CafeteriaReportKind.RefundVoidAudit, CafeteriaReportKind.AdditionalCreditAudit, CafeteriaReportKind.ExternalSystemReconciliation } }
        })
        {
            var parent = new MenuItem
            {
                Header = group.Name,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 45, 68))
            };
            foreach (var kind in group.Kinds) parent.Items.Add(BuildReportMenuItem(kind));
            reports.Items.Add(parent);
        }
        menu.Items.Add(reports);

        MenuItem administration = new()
        {
              Header = "Administration"
            , FontWeight = FontWeight.Bold
            , Foreground = new SolidColorBrush(Color.FromRgb(24, 45, 68))
        };
        MenuItem priceMaintenance = new() { Header = "Price Maintenance..." };
        priceMaintenance.Click += async (_, _) => await OpenProductMaintenanceAsync();
        administration.Items.Add(priceMaintenance);

        MenuItem emailPreferences = new() { Header = "Employee Email Preferences..." };
        emailPreferences.Click += async (_, _) => await OpenEmployeeEmailPreferencesAsync();
        administration.Items.Add(emailPreferences);

        administration.Items.Add(new Separator());
        MenuItem diagnostics = new() { Header = "Diagnostics" };
        diagnostics.Click += (_, _) => OpenEngineeringDiagnostics();
        administration.Items.Add(diagnostics);
        menu.Items.Add(administration);

        return menu;
    }

    private async Task OpenEmployeeEmailPreferencesAsync()
    {
        CafeteriaServicesClient servicesClient = this.services.GetRequiredService<CafeteriaServicesClient>();
        EmployeeEmailPreferencesWindow window = new(this.identification, servicesClient);
        await window.ShowDialog(this);
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

    private MenuItem BuildReportMenuItem(CafeteriaReportKind kind)
    {
        ReportCatalogItem metadata = CafeteriaReportCatalog.All.Single(item => item.Kind == kind);
        MenuItem reportItem = new()
        {
            Header = metadata.Name
        };
        ToolTip.SetTip(reportItem, metadata.Description);
        reportItem.Click += async (_, _) => await OpenReportCenterAsync(kind);
        return reportItem;
    }

    private async Task OpenReportCenterAsync(CafeteriaReportKind kind)
    {
        ICafeteriaReportService reportService = this.services.GetRequiredService<ICafeteriaReportService>();
        IEnumerable<IReportExporter> exporters = this.services.GetServices<IReportExporter>();
        ReportCenterWindow window = new(reportService, this.identification, this.products, exporters, kind);
        await window.ShowDialog(this);
        RegisterActivity();
    }

    private Control BuildHeader()
    {
        var navy = new SolidColorBrush(Color.FromRgb(24, 45, 68));
        var headerGrid = new Grid { ColumnSpacing = 18 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        var brand = new StackPanel { Spacing = 2 };
        brand.Children.Add(new TextBlock { Text = "CAFETERIA", Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeight.Bold });
        brand.Children.Add(new TextBlock { Text = "Cafeteria", Foreground = new SolidColorBrush(Color.FromRgb(193, 210, 225)), FontSize = 13 });
        headerGrid.Children.Add(brand);
        var identifyBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        identifyBar.Children.Add(lookup); identifyBar.Children.Add(findButton); identifyBar.Children.Add(cashCustomerButton);
        Grid.SetColumn(identifyBar, 1);
        headerGrid.Children.Add(identifyBar);
        return new Border { Background = navy, Padding = new Thickness(20, 14), Child = headerGrid };
    }

    private Control BuildRecentTransactionsPanel()
    {
        var panel = new Border { Background = Brushes.White, Margin = new Thickness(18, 0, 18, 10), Padding = new Thickness(18, 8), CornerRadius = new CornerRadius(6), IsVisible = uiOptions.RecentTransactionCount > 0 };
        recentTransactionsPanel = panel;
        var grid = new Grid { RowSpacing = 4 };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        grid.Children.Add(new TextBlock { Text = "RECENT TRANSACTIONS — TODAY", FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.FromRgb(24, 45, 68)) });
        Grid.SetRow(recentTransactions, 1);
        grid.Children.Add(recentTransactions);
        panel.Child = grid;
        return panel;
    }

    private Control BuildCustomerPanel()
    {
        var panel = new Grid { RowSpacing = 8 };
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        identity.Children.Add(new Border { BorderThickness = new Thickness(1), Padding = new Thickness(3), Child = photo });
        var identityText = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        identityText.Children.Add(customerName);
        identityText.Children.Add(period);
        identityText.Children.Add(fundingStatus);
        identity.Children.Add(identityText);
        panel.Children.Add(identity);

        var finances = new Grid { ColumnSpacing = 12, RowSpacing = 5, Margin = new Thickness(0, 6, 0, 4) };
        finances.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        finances.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        AddFinancialRow(finances, 0, "Monthly Cafeteria Credit", creditLimit);
        AddFinancialRow(finances, 1, "Company Subsidy Remaining", subsidyRemaining);
        AddFinancialRow(finances, 2, "Employee-Funded Remaining", nonSubsidyRemaining);
        AddFinancialRow(finances, 3, "Total Available to Spend", available);
        AddFinancialRow(finances, 4, "Debt Brought Forward", carriedDebt);
        AddFinancialRow(finances, 5, "Customer Responsibility (before payments)", customerLiability);
        AddFinancialRow(finances, 6, "Customer Payments", customerPaymentsValue);
        AddFinancialRow(finances, 7, "Employee Amount Due", employeeAmountDue);
        Grid.SetRow(finances, 1);
        panel.Children.Add(finances);

        var paymentAction = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F0F8F3")),
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(5),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock { Text = "CUSTOMER PAYMENT", FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#17774C")) },
                            new TextBlock { Text = "Record money received from the employee to reduce the Employee Amount Due shown above.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 }
                        }
                    },
                    customerPaymentButton
                }
            }
        };
        Grid.SetColumn(customerPaymentButton, 1);
        Grid.SetRow(paymentAction, 2);
        panel.Children.Add(paymentAction);

        var searchPanel = new StackPanel { Spacing = 3 };
        searchPanel.Children.Add(new TextBlock { Text = "Search Results (double-click to select)", FontWeight = Avalonia.Media.FontWeight.Bold });
        searchPanel.Children.Add(searchResults);
        Grid.SetRow(searchPanel, 3);
        panel.Children.Add(searchPanel);

        var txPanel = new Grid { ColumnSpacing = 8 };
        txPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        txPanel.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        txPanel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        txPanel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        txPanel.Children.Add(new TextBlock { Text = "This Month's Transactions", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(0, 3, 0, 5) });
        Grid.SetColumn(refundButton, 1);
        txPanel.Children.Add(refundButton);
        Grid.SetRow(transactions, 1);
        Grid.SetColumnSpan(transactions, 2);
        txPanel.Children.Add(transactions);
        Grid.SetRow(txPanel, 4);
        panel.Children.Add(txPanel);
        return panel;
    }

    private Control BuildIdleDashboard()
    {
        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        StackPanel heading = new() { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = "DAILY DASHBOARD", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#182D44")) });
        heading.Children.Add(new TextBlock { Text = "Today's KPIs with this week's Monday–Friday sales trend.", Foreground = new SolidColorBrush(Color.Parse("#687584")) });
        root.Children.Add(heading);

        var metrics = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        metrics.Children.Add(DashboardMetric("Net Sales", todayNetSales));
        var tx = DashboardMetric("Transactions", todayTransactions); Grid.SetColumn(tx, 1); metrics.Children.Add(tx);
        var rf = DashboardMetric("Refunds", todayRefunds); Grid.SetColumn(rf, 2); metrics.Children.Add(rf);
        Grid.SetRow(metrics, 1); root.Children.Add(metrics);

        var charts = new Grid { ColumnSpacing = 16, MinHeight = 225 };
        charts.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        charts.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        charts.Children.Add(weeklySalesByDay);
        Grid.SetColumn(todayItemsByValue, 1);
        charts.Children.Add(todayItemsByValue);
        Grid.SetRow(charts, 2);
        root.Children.Add(charts);

        StackPanel ready = new() { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        ready.Children.Add(new TextBlock { Text = "Ready for customer", FontSize = 16, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#182D44")) });
        ready.Children.Add(new TextBlock { Text = "Scan a card, enter an identifier, or press Find with an empty search box to browse customers.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#687584")) });
        Grid.SetRow(ready, 3);
        root.Children.Add(ready);

        Grid footer = new() { ColumnSpacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        footer.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        footer.Children.Add(todayDashboardStatus);
        Button openReport = new() { Content = "Open Daily Sales Report", MinWidth = 185, MinHeight = 38 };
        openReport.Click += async (_, _) => await OpenReportCenterAsync(CafeteriaReportKind.DailySales);
        Grid.SetColumn(openReport, 1);
        footer.Children.Add(openReport);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);

        return root;
    }

    private static Border DashboardMetric(string caption, TextBlock value)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = caption.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.Bold });
        stack.Children.Add(value);
        return new Border { Background = new SolidColorBrush(Color.Parse("#F4F7FA")), Padding = new Thickness(14), CornerRadius = new CornerRadius(6), Child = stack };
    }

    private Control BuildOrderPanel()
    {
        var panel = new Grid { RowSpacing = 8 };
        panel.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        panel.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(new GridLength(0.7, GridUnitType.Star)));

        var productPanel = new Grid { RowSpacing = 4 };
        productPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        productPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        productPanel.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        productPanel.Children.Add(new TextBlock { Text = "Products (double-click to add)", FontWeight = Avalonia.Media.FontWeight.Bold });
        Grid.SetRow(productSearch, 1);
        productPanel.Children.Add(productSearch);
        Grid.SetRow(productList, 2);
        productPanel.Children.Add(productList);
        panel.Children.Add(productPanel);

        var cartPanel = new Grid { RowSpacing = 4 };
        cartPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        cartPanel.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        cartPanel.Children.Add(new TextBlock { Text = "Current Order (double-click to remove)", FontWeight = Avalonia.Media.FontWeight.Bold });
        Grid.SetRow(cartList, 1);
        cartPanel.Children.Add(cartList);
        Grid.SetRow(cartPanel, 1);
        panel.Children.Add(cartPanel);

        Grid.SetRow(cartTotal, 2);
        panel.Children.Add(cartTotal);

        var tenders = new Grid { ColumnSpacing = 8 };
        tenders.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        tenders.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        tenders.Children.Add(cafeteriaTender);
        Grid.SetColumn(cashTender, 1);
        tenders.Children.Add(cashTender);
        Grid.SetRow(tenders, 3);
        panel.Children.Add(tenders);

        var paymentSummary = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        paymentSummary.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        paymentSummary.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        paymentSummary.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        paymentSummary.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        paymentSummary.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        paymentSummary.Children.Add(paymentRemaining);
        Grid.SetColumn(changeDue, 1);
        paymentSummary.Children.Add(changeDue);
        Grid.SetRow(newResponsibility, 1);
        Grid.SetColumnSpan(newResponsibility, 2);
        paymentSummary.Children.Add(newResponsibility);
        var payLaterHint = new TextBlock
        {
            Text = "Enter cash received and the Cafeteria Account charge account amount adjusts automatically. Amounts above remaining company subsidy become customer responsibility for later payment or employee amount due.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        };
        Grid.SetRow(payLaterHint, 2);
        Grid.SetColumnSpan(payLaterHint, 2);
        paymentSummary.Children.Add(payLaterHint);
        Grid.SetRow(paymentSummary, 4);
        panel.Children.Add(paymentSummary);

        var buttons = new Grid { ColumnSpacing = 8, MinWidth = 540 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        buttons.Children.Add(checkoutButton);
        Grid.SetColumn(voidButton, 1);
        buttons.Children.Add(voidButton);
        Grid.SetColumn(clearButton, 2);
        buttons.Children.Add(clearButton);
        var actionHost = new StackPanel { Spacing = 4 };
        actionHost.Children.Add(voidAvailability);
        actionHost.Children.Add(buttons);
        Grid.SetRow(actionHost, 6);
        panel.Children.Add(actionHost);
        return panel;
    }

    private async Task UpdateCacheStatusAsync()
    {
        try
        {
            var cache = await employeeCacheStatusProvider.GetAsync();
            var age = cache.Age(DateTimeOffset.UtcNow);
            if (cache.EmployeeCount == 0) cacheStatus.Text = "Employee cache: EMPTY";
            else if (!age.HasValue) cacheStatus.Text = $"Employee cache: {cache.EmployeeCount:N0} • age unknown";
            else if (age.Value.TotalHours < 1) cacheStatus.Text = $"Employee cache: {cache.EmployeeCount:N0} • {(int)age.Value.TotalMinutes}m old";
            else cacheStatus.Text = $"Employee cache: {cache.EmployeeCount:N0} • {age.Value.TotalHours:0.0}h old";
            var ageMinutes = age?.TotalMinutes;
            cacheStatus.Foreground = cache.EmployeeCount == 0 || ageMinutes >= healthOptions.EmployeeCacheCriticalMinutes
                ? Brushes.Red
                : ageMinutes >= healthOptions.EmployeeCacheWarningMinutes
                    ? Brushes.Orange
                    : Brushes.Green;
            if (!string.IsNullOrWhiteSpace(cache.LastError))
            {
                cacheStatus.Text += " • last refresh failed";
                if (cacheStatus.Foreground == Brushes.Green) cacheStatus.Foreground = Brushes.Orange;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read employee cache status");
            cacheStatus.Text = "Employee cache: status unavailable";
        }
    }

    private async Task RefreshCustomersAsync()
    {
        var refresh = services.GetService<ReferenceDataRefreshService>();
        if (refresh is null) return;
        try
        {
            refreshEmployeesButton.IsEnabled = false;
            cacheStatus.Text = "Reference data: refreshing...";
            await refresh.RefreshAsync(DateOnly.FromDateTime(DateTime.Today));
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reference data refresh failed");
            status.Text = $"Reference refresh failed: {ex.GetBaseException().Message}";
        }
        finally
        {
            refreshEmployeesButton.IsEnabled = true;
            await UpdateCacheStatusAsync();
        }
    }

    private async Task SearchNamesAsync()
    {
        var value = lookup.Text?.Trim() ?? string.Empty;
        if (!CustomerLookupResolver.LooksLikeNameSearch(value)) return;
        nameSearchCancellation?.Cancel();
        nameSearchCancellation?.Dispose();
        nameSearchCancellation = new CancellationTokenSource();
        var ct = nameSearchCancellation.Token;
        try
        {
            currentCandidates = await identification.SearchAsync(value, 20, ct);
            if (ct.IsCancellationRequested) return;
            searchResults.ItemsSource = currentCandidates.Select(x => new SearchRow(x)).ToArray();
            status.Text = currentCandidates.Count == 0 ? "No matching employee found" : $"{currentCandidates.Count} employee match{(currentCandidates.Count == 1 ? string.Empty : "es")}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live employee name search failed");
            status.Text = "Employee search unavailable";
        }
    }

    private async Task LoadRecentTransactionsAsync()
    {
        if (uiOptions.RecentTransactionCount <= 0)
        {
            recentTransactions.ItemsSource = Array.Empty<string>();
            return;
        }
        try
        {
            var rows = await recentTransactionQuery.GetTodayAsync(uiOptions.RecentTransactionCount);
            recentTransactions.ItemsSource = rows.Select(x => $"{x.CompletedAt.LocalDateTime:HH:mm}   {x.CustomerName}   {Money(x.Total)}   {x.TenderSummary}").ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load recent POS transactions");
        }
    }

    private void RegisterActivity() => lastActivityUtc = DateTimeOffset.UtcNow;

    private void CheckCustomerInactivity()
    {
        if (uiOptions.CustomerInactivityTimeoutSeconds <= 0 || currentCustomer is null) return;
        if (DateTimeOffset.UtcNow - lastActivityUtc < TimeSpan.FromSeconds(uiOptions.CustomerInactivityTimeoutSeconds)) return;
        logger.LogInformation("Clearing customer {CustomerId} after {TimeoutSeconds} seconds of Linux POS inactivity", currentCustomer.Customer.Id, uiOptions.CustomerInactivityTimeoutSeconds);
        ClearCustomer();
        RegisterActivity();
    }

    private async Task IdentifyAsync()
    {
        var value = lookup.Text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            currentCandidates = await identification.SearchAsync(string.Empty, 50);
            searchResults.ItemsSource = currentCandidates.Select(x => new SearchRow(x)).ToArray();
            status.Text = currentCandidates.Count == 0
                ? "No customers are available in the local employee cache"
                : $"Browsing {currentCandidates.Count} customers - double-click the correct customer";
            FocusCustomerLookup();
            return;
        }

        try
        {
            status.Text = "Identifying customer...";
            var result = await identification.ResolveAsync(value, 50);
            if (result.Account is not null)
            {
                lookup.Text = string.Empty;
                ShowCustomer(result.Account);
                productSearch.Focus();
                return;
            }

            currentCandidates = result.Candidates;
            searchResults.ItemsSource = currentCandidates.Select(x => new SearchRow(x)).ToArray();
            if (currentCandidates.Count == 0)
            {
                status.Text = "No matching customer found";
                FocusCustomerLookup(selectAll: true);
            }
            else
            {
                status.Text = $"{currentCandidates.Count} matches - double-click the correct customer";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Universal customer identification failed");
            status.Text = $"Identification error: {ex.Message}";
            FocusCustomerLookup(selectAll: true);
        }
    }

    private void FocusCustomerLookup(bool selectAll = false)
    {
        if (currentCustomer is not null) return;
        lookup.Focus();
        if (selectAll) lookup.SelectAll();
    }

    private async Task LoadSelectedCandidateAsync()
    {
        if (searchResults.SelectedItem is SearchRow row)
            await LoadCustomerAsync(row.Customer.CustomerId);
    }

    private async Task LoadCustomerAsync(int customerId)
    {
        var account = await identification.LoadAsync(customerId);
        if (account is not null) ShowCustomer(account);
    }

    private void ShowCustomer(CustomerAccountView account)
    {
        currentCustomer = account;
        if (orderWorkspace is not null) orderWorkspace.IsVisible = true;
        if (idleDashboard is not null) idleDashboard.IsVisible = false;
        UpdateRecentTransactionsVisibility();
        customerName.Text = account.Customer.Name;
        period.Text = account.Customer.IsCashCustomer ? "Cash-only sale" : $"{new DateTime(account.Period.Year, account.Period.Month, 1):MMMM yyyy} | Eligible days: {account.Period.EligibleWorkingDays}";
        if (account.Customer.IsCashCustomer || !account.Customer.IsEligibleForCredit)
        {
            fundingStatus.Text = account.Customer.IsCashCustomer
                ? "Cash customer - cash tender only"
                : "Not eligible for cafeteria credit - cash purchases only";
            foreach (var value in new[] { creditLimit, subsidyRemaining, nonSubsidyRemaining, available, carriedDebt, customerLiability, customerPaymentsValue, employeeAmountDue }) value.Text = "—";
        }
        else
        {
            fundingStatus.Text = account.Funding?.IsAuthoritative == true
                ? "Funding current"
                : $"Funding unavailable | {account.Funding?.StatusMessage ?? "Reference data not loaded"}";
            creditLimit.Text = Money(account.FinancialStatus.CreditLimit);
            subsidyRemaining.Text = Money(account.FinancialStatus.SubsidyAmountRemaining);
            nonSubsidyRemaining.Text = Money(account.FinancialStatus.NonSubsidyAmountRemaining);
            available.Text = Money(account.FinancialStatus.TotalAvailableToSpend);
            carriedDebt.Text = Money(account.FinancialStatus.CarriedForwardDebt);
            customerLiability.Text = Money(account.FinancialStatus.GrossCustomerLiability);
            customerPaymentsValue.Text = Money(account.FinancialStatus.CustomerPayments);
            employeeAmountDue.Text = Money(account.FinancialStatus.EmployeeAmountDue);
        }
        transactions.ItemsSource = account.Transactions.Select(x => new TransactionRow(x)).ToArray();
        SetPhoto(account.Customer.Photo);
        status.Text = account.Customer.IsCashCustomer
            ? "Cash customer ready"
            : "Cafeteria Services synchronization active";
        customerPaymentButton.IsEnabled = CustomerPaymentPolicy.Evaluate(account).Allowed;
        UpdateCustomerDisplay(account);
        UpdateCheckoutState();
    }

    private async Task LoadProductsAsync(string? search = null)
    {
        try
        {
            currentProducts = string.IsNullOrWhiteSpace(search)
                ? await products.GetAllAsync()
                : await products.SearchAsync(search, 50);
            productList.ItemsSource = currentProducts.Select(x => new ProductRow(x)).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Product catalog load failed");
            status.Text = "Product catalog could not be loaded";
        }
    }

    private void AddSelectedProduct()
    {
        if (productList.SelectedItem is not ProductRow row) return;
        var product = row.Product;
        var existing = cart.FirstOrDefault(x => x.ProductId == product.Id);
        if (existing is null) cart.Add(new CartRow(product.Id, product.Name, product.Price, 1));
        else existing.Quantity++;
        RefreshCart();
    }

    private void RemoveSelectedCartRow()
    {
        if (cartList.SelectedItem is not CartDisplayRow selected) return;
        cart.RemoveAll(x => x.ProductId == selected.ProductId);
        RefreshCart();
    }

    private void RefreshCart()
    {
        cartList.ItemsSource = cart.Select(x => new CartDisplayRow(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity, x.Total)).ToArray();
        var total = cart.Sum(x => x.Total);
        cartTotal.Text = $"Total: {Money(total)}";
        if (currentCustomer is not null)
        {
            TenderCustomerContext tenderContext = TenderCustomerPolicy.Evaluate(currentCustomer);
            decimal accountAmount = TenderQuoteCalculator.DefaultCafeteriaAccountAmount
            (
                  total
                , tenderContext.AvailableToSpend
                , tenderContext.CafeteriaAccountAllowed
            );
            cafeteriaTender.Text = accountAmount.ToString("0.00");
            cashTender.Text = "0.00";
        }
        UpdatePaymentState();
    }

    private async Task CheckoutAsync()
    {
        if (currentCustomer is null || cart.Count == 0) return;
        if (!TryGetPayment(out var accountAmount, out _, out var cashApplied, out TenderQuote? quote))
        {
            status.Text = "Enter valid cafeteria-account and cash amounts";
            return;
        }
        if (quote is null || !quote.IsComplete)
        {
            status.Text = $"Payment incomplete - {Money(quote?.RemainingAmount ?? cart.Sum(item => item.Total))} still due";
            return;
        }

        try
        {
            checkoutButton.IsEnabled = false;
            await checkout.CompleteAsync(
                currentCustomer,
                cart.Select(x => new PosOrderLine(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity)).ToArray(),
                accountAmount,
                cashApplied,
                operatorSession.Current?.UserId ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required."),
                persistenceOptions.TerminalId);
            cart.Clear();
            RefreshCart();
            if (!currentCustomer.Customer.IsCashCustomer)
            {
                currentCustomer = await identification.LoadAsync(currentCustomer.Customer.Id);
                if (currentCustomer is not null) ShowCustomer(currentCustomer);
            }
            else
            {
                ShowCustomer(CashCustomerAccountViewFactory.Create(DateOnly.FromDateTime(DateTime.Today)));
            }
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
            status.Text = "Sale completed locally; synchronization queued";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checkout failed for customer {CustomerId}", currentCustomer.Customer.Id);
            status.Text = $"Checkout error: {ex.Message}";
        }
        finally
        {
            UpdateCheckoutState();
        }
    }

    private async Task RecordCustomerPaymentAsync()
    {
        if (currentCustomer is null) return;
        CustomerPaymentEligibility eligibility = CustomerPaymentPolicy.Evaluate(currentCustomer);
        if (!eligibility.Allowed)
        {
            status.Text = eligibility.Reason ?? "Customer payment is not available.";
            return;
        }

        decimal due = eligibility.MaximumPayment;
        var dialog = new CustomerPaymentWindow(currentCustomer.Customer.Name, due);
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed) return;

        try
        {
            customerPaymentButton.IsEnabled = false;
            var operatorId = operatorSession.Current?.UserId ?? throw new UnauthorizedAccessException("An authenticated cafeteria operator is required.");
            await customerPayments.RecordAsync(currentCustomer, dialog.PaymentAmount, dialog.PaymentMethod, operatorId, dialog.Reference);
            var customerId = currentCustomer.Customer.Id;
            var refreshed = await identification.LoadAsync(customerId);
            if (refreshed is not null) ShowCustomer(refreshed);
            status.Text = $"Customer payment recorded: {Money(dialog.PaymentAmount)}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Customer payment failed for customer {CustomerId}", currentCustomer.Customer.Id);
            status.Text = $"Customer payment error: {ex.Message}";
        }
        finally
        {
            if (currentCustomer is not null)
                customerPaymentButton.IsEnabled = CustomerPaymentPolicy.Evaluate(currentCustomer).Allowed;
        }
    }

    private Task OnCustomerAccountChangedAsync(int customerId)
    {
        if (currentCustomer?.Customer.Id != customerId) return Task.CompletedTask;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var refreshed = await identification.LoadAsync(customerId);
                if (refreshed is not null) ShowCustomer(refreshed);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not refresh displayed customer {CustomerId}", customerId);
            }
        });
        return Task.CompletedTask;
    }

    private async Task OpenProductMaintenanceAsync()
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
        ProductMaintenanceWindow window = new(maintenance);
        await window.ShowDialog(this);
        await LoadProductsAsync();
    }

    private async Task<OperatorIdentity?> AuthorizeManagerAsync(string action)
    {
        var current = operatorSession.Current;
        if (current?.IsManager == true)
        {
            managerAuthorization.Grant(current);
            return current;
        }
        var dialog = new ManagerAuthorizationWindow(operatorAuthenticator, managerAuthorization, action);
        return await dialog.ShowDialog<OperatorIdentity?>(this);
    }

    private async Task VoidCurrentSaleAsync()
    {
        if (currentCustomer is null || cart.Count == 0) return;
        var manager = await AuthorizeManagerAsync("Void current sale");
        if (manager is null) return;
        var reason = await new ReasonWindow("Void Sale", "Reason for voiding this sale:").ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(reason)) return;
        try
        {
            await voidService.VoidCurrentSaleAsync(
                currentCustomer,
                cart.Select(x => new PosOrderLine(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity)).ToArray(),
                manager.UserId,
                persistenceOptions.TerminalId,
                reason);
            cart.Clear();
            RefreshCart();
            status.Text = $"Sale voided by {manager.UserId}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Void failed for operator {ManagerUserId}", manager.UserId);
            status.Text = $"Void error: {ex.GetBaseException().Message}";
        }
    }

    private async Task RefundSelectedAsync()
    {
        if (currentCustomer is null) return;
        if (transactions.SelectedItem is not TransactionRow selected)
        {
            status.Text = "Select a purchase from this month's transactions to refund.";
            return;
        }

        if (string.Equals(selected.Transaction.Kind, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Refund entries cannot be refunded. Select the original purchase instead.";
            return;
        }

        if (!string.Equals(selected.Transaction.Kind, "Purchase", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "Select a purchase from this month's transactions to refund.";
            return;
        }

        var order = await orderQuery.GetAsync(selected.Transaction.ReferenceId);
        RefundEligibility refundEligibility = RefundEligibilityPolicy.Evaluate(order);
        if (!refundEligibility.Allowed || order is null)
        {
            status.Text = refundEligibility.Reason ?? "The selected order is not refundable.";
            return;
        }

        var refundDialog = new RefundWindow(order);
        var confirmed = await refundDialog.ShowDialog<bool>(this);
        if (!confirmed) return;
        var manager = await AuthorizeManagerAsync("Refund completed sale");
        if (manager is null) return;
        string defaultReason = RefundReasonGenerator.Generate(order, refundDialog.SelectedItems);
        string? reason = await new ReasonWindow("Refund Sale", "Reason for refund:", defaultReason).ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            var accountBeforeRefund = currentCustomer;
            var refund = refundService.Refund(order, refundDialog.SelectedItems, refundDialog.CafeteriaAccountAmount, refundDialog.CashAmount, manager.UserId, DateTimeOffset.Now, reason);
            await persistenceStore.SaveOrderAsync(order);

            if (!accountBeforeRefund.Customer.IsCashCustomer)
            {
                currentCustomer = await identification.LoadAsync(accountBeforeRefund.Customer.Id);
                if (currentCustomer is not null) ShowCustomer(currentCustomer);
            }
            await LoadRecentTransactionsAsync();
            await LoadIdleDashboardAsync();
            status.Text = $"Refund completed by {manager.UserId}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Refund failed for order {OrderId}", selected.Transaction.ReferenceId);
            status.Text = $"Refund error: {ex.GetBaseException().Message}";
        }
    }

    private void CancelActiveTransaction()
    {
        if (currentCustomer is null) return;

        // Escape abandons only the in-memory, uncompleted customer/cart state.
        // It does not create an order, void, refund, or external-system event.
        ClearCustomer();
        status.Text = "Sale cancelled";
    }

    private void InitializeCustomerDisplay()
    {
        var primary = Screens.Primary;
        customerDisplayScreen = Screens.All.FirstOrDefault(x => !ReferenceEquals(x, primary));
        if (customerDisplayScreen is null)
        {
            logger.LogInformation("No secondary monitor detected. Linux customer-facing display is disabled.");
            return;
        }
        customerDisplay = new CustomerDisplayWindow();
        customerDisplay.PlaceOn(customerDisplayScreen.WorkingArea, customerDisplayScreen.Scaling);
        logger.LogInformation("Linux customer-facing display enabled on secondary monitor {Bounds}", customerDisplayScreen.WorkingArea);
    }

    private void UpdateCustomerDisplay(CustomerAccountView account)
    {
        if (customerDisplay is null || customerDisplayScreen is null) return;
        customerDisplay.PlaceOn(customerDisplayScreen.WorkingArea, customerDisplayScreen.Scaling);
        customerDisplay.ShowCustomer(account);
        if (!customerDisplay.IsVisible) customerDisplay.Show();
    }

    private void ClearCustomer()
    {
        currentCustomer = null;
        if (orderWorkspace is not null) orderWorkspace.IsVisible = false;
        if (idleDashboard is not null) idleDashboard.IsVisible = true;
        UpdateRecentTransactionsVisibility();
        cart.Clear();
        RefreshCart();
        transactions.ItemsSource = null;
        customerPaymentButton.IsEnabled = false;
        newResponsibility.Text = "New Customer Responsibility: $0.00";
        customerDisplay?.ClearCustomer();
        searchResults.ItemsSource = null;
        currentCandidates = Array.Empty<CustomerSearchCandidate>();
        customerName.Text = "No customer selected";
        fundingStatus.Text = string.Empty;
        period.Text = string.Empty;
        foreach (var value in new[] { creditLimit, subsidyRemaining, nonSubsidyRemaining, available, carriedDebt, customerLiability, employeeAmountDue }) value.Text = "$0.00";
        if (photo.Source is Bitmap old) old.Dispose();
        photo.Source = null;
        FocusCustomerLookup();
    }


    private void UpdateRecentTransactionsVisibility()
    {
        if (recentTransactionsPanel is null || recentTransactionsRow is null) return;

        // Match Windows: recent terminal activity is shown only while no customer
        // is selected. An active customer/sale gets the full transaction workspace.
        var show = uiOptions.RecentTransactionCount > 0 && currentCustomer is null;
        recentTransactionsPanel.IsVisible = show;
        recentTransactionsRow.Height = show ? new GridLength(165) : new GridLength(0);
    }

    private void SetPhoto(byte[]? bytes)
    {
        if (photo.Source is Bitmap old) old.Dispose();
        photo.Source = null;
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using var stream = new MemoryStream(bytes);
            photo.Source = new Bitmap(stream);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid customer photo for customer {CustomerId}", currentCustomer?.Customer.Id);
        }
    }

    private void UpdateCheckoutState()
    {
        var hasCustomer = currentCustomer is not null;
        var hasOrder = hasCustomer && cart.Count > 0;
        checkoutButton.IsEnabled = hasOrder &&
            TryGetPayment(out _, out _, out TenderQuote? quote) &&
            quote is not null &&
            quote.IsComplete;
        voidButton.IsEnabled = operatorSession.IsAuthenticated && hasOrder;
        refundButton.IsEnabled = operatorSession.IsAuthenticated && hasCustomer;

        if (!operatorSession.IsAuthenticated)
        {
            voidAvailability.Text = "VOID CURRENT SALE: sign in as an operator first.";
            voidAvailability.Foreground = Brushes.Gray;
        }
        else if (!hasCustomer)
        {
            voidAvailability.Text = "VOID CURRENT SALE: select a customer and add an item first.";
            voidAvailability.Foreground = Brushes.Gray;
        }
        else if (!hasOrder)
        {
            voidAvailability.Text = "VOID CURRENT SALE becomes available after the first item is added.";
            voidAvailability.Foreground = Brushes.Gray;
        }
        else
        {
            voidAvailability.Text = "VOID AVAILABLE • Voids the current uncompleted sale • Manager approval and reason required.";
            voidAvailability.Foreground = Brushes.Red;
        }
    }


    private void UpdateAccountTenderFromCash()
    {
        if (this.updatingTender)
        {
            return;
        }

        if (!decimal.TryParse(this.cashTender.Text, out decimal cashReceived) || cashReceived < 0m)
        {
            UpdatePaymentState();
            return;
        }

        decimal total = this.cart.Sum(item => item.Total);
        TenderCustomerContext tenderContext = this.currentCustomer is null
            ? new TenderCustomerContext(false, false, 0m, 0m)
            : TenderCustomerPolicy.Evaluate(this.currentCustomer);
        decimal accountAmount = TenderQuoteCalculator.DefaultCafeteriaAccountAmount
        (
              total
            , tenderContext.AvailableToSpend
            , tenderContext.CafeteriaAccountAllowed
            , cashReceived
        );

        this.updatingTender = true;
        try
        {
            this.cafeteriaTender.Text = accountAmount.ToString("0.00");
        }
        finally
        {
            this.updatingTender = false;
        }

        UpdatePaymentState();
    }

    private void UpdatePaymentState()
    {
        if (!TryGetPayment(out _, out _, out TenderQuote? quote) || quote is null)
        {
            paymentRemaining.Text = "Remaining: —";
            changeDue.Text = "Change: —";
            checkoutButton.IsEnabled = false;
            return;
        }

        paymentRemaining.Text = $"Remaining: {Money(quote.RemainingAmount)}";
        changeDue.Text = $"Change: {Money(quote.ChangeAmount)}";
        newResponsibility.Text = $"New Customer Responsibility: {Money(quote.NewCustomerResponsibility)}";
        paymentRemaining.Foreground = quote.RemainingAmount > 0m ? Brushes.Red : Brushes.Green;
        changeDue.Foreground = quote.ChangeAmount > 0m ? Brushes.Green : Brushes.Gray;
        UpdateCheckoutState();
    }

    private bool TryGetPayment
    (
          out decimal accountAmount
        , out decimal cashReceived
        , out decimal cashApplied
        , out TenderQuote? quote
    )
    {
        accountAmount = 0m;
        cashReceived = 0m;
        cashApplied = 0m;
        quote = null;

        if (!decimal.TryParse(cafeteriaTender.Text, out accountAmount)
            || !decimal.TryParse(cashTender.Text, out cashReceived)
            || accountAmount < 0m
            || cashReceived < 0m)
        {
            return false;
        }

        decimal total = cart.Sum(item => item.Total);
        TenderCustomerContext tenderContext = currentCustomer is null
            ? new TenderCustomerContext(false, false, 0m, 0m)
            : TenderCustomerPolicy.Evaluate(currentCustomer);

        try
        {
            quote = TenderQuoteCalculator.Calculate
            (
                  total
                , tenderContext.AvailableToSpend
                , tenderContext.SubsidyRemaining
                , tenderContext.CafeteriaAccountAllowed
                , creditSalesAllowed: false
                , cafeteriaAccountAmount: accountAmount
                , cashReceived: cashReceived
                , useEmployeeCredit: false
            );
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        cashApplied = quote.CashApplied;
        return true;
    }

    private void ApplyTheme()
    {
        var navy = new SolidColorBrush(Color.Parse("#182D44"));
        var blue = new SolidColorBrush(Color.Parse("#1F6FBE"));
        var green = new SolidColorBrush(Color.Parse("#17774C"));
        var gray = new SolidColorBrush(Color.Parse("#64707E"));
        findButton.Background = blue; findButton.Foreground = Brushes.White;
        cashCustomerButton.Background = green; cashCustomerButton.Foreground = Brushes.White;
        checkoutButton.Background = green; checkoutButton.Foreground = Brushes.White;
        clearButton.Background = gray; clearButton.Foreground = Brushes.White;
        voidButton.Background = Brushes.Firebrick; voidButton.Foreground = Brushes.White;
        refundButton.Background = Brushes.Firebrick; refundButton.Foreground = Brushes.White;
        customerName.Foreground = navy; available.Foreground = blue;
    }

    private void UpdateOperatorStatus()
    {
        var current = operatorSession.Current;
        operatorStatus.Text = current is null
            ? "Operator: not signed in"
            : $"Operator: {current.DisplayName} ({current.UserId})" + (current.IsManager ? " • Manager" : string.Empty);
        operatorStatus.Foreground = current is null ? Brushes.Red : Brushes.Green;
        UpdateCheckoutState();
    }

    private async Task LoadIdleDashboardAsync()
    {
        try
        {
            var reportService = services.GetRequiredService<ICafeteriaReportService>();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var report = await reportService.GenerateAsync(CafeteriaReportKind.DailySales, new ReportFilter(today, today));
            decimal Metric(string label) => report.Metrics.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase))?.Value ?? 0m;
            todayNetSales.Text = Money(Metric("Net sales"));
            todayTransactions.Text = Metric("Transactions").ToString("N0");
            todayRefunds.Text = Money(Metric("Refunds"));

            int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
            DateOnly monday = today.AddDays(-daysSinceMonday);
            DateOnly friday = monday.AddDays(4);
            var weeklyReport = await reportService.GenerateAsync(CafeteriaReportKind.DailySales, new ReportFilter(monday, friday));
            var weeklyRows = weeklyReport.Tables
                .FirstOrDefault(table => string.Equals(table.Title, "Daily detail", StringComparison.OrdinalIgnoreCase))?
                .Rows ?? [];

            List<(string Label, decimal Value)> weekdayValues = [];
            for (int dayOffset = 0; dayOffset < 5; dayOffset++)
            {
                DateOnly day = monday.AddDays(dayOffset);
                IReadOnlyDictionary<string, object?>? row = weeklyRows.FirstOrDefault(item =>
                {
                    object? dateValue = item.GetValueOrDefault("date");
                    return dateValue is DateOnly reportDate && reportDate == day;
                });

                decimal value = row is null
                    ? 0m
                    : Convert.ToDecimal(row.GetValueOrDefault("net") ?? 0m);

                weekdayValues.Add((day.ToString("dddd"), value));
            }

            weeklySalesByDay.SetData("SALES VALUE BY DAY — THIS WEEK", weekdayValues, "C0");

            var productReport = await reportService.GenerateAsync(CafeteriaReportKind.ProductSales, new ReportFilter(today, today));
            var productRows = productReport.Tables.FirstOrDefault(x => string.Equals(x.Title, "Product detail", StringComparison.OrdinalIgnoreCase))?.Rows ?? [];
            var productSales = productRows.Select(row => new
            {
                Name = Convert.ToString(row.GetValueOrDefault("product")) ?? "Unknown",
                Value = Convert.ToDecimal(row.GetValueOrDefault("sales") ?? 0m)
            }).ToArray();
            todayItemsByValue.SetData("ITEMS SOLD — VALUE", DashboardPieBuckets(productSales.Select(x => (x.Name, x.Value))), "C0");
            todayDashboardStatus.Text = $"Updated {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load Linux daily dashboard");
            todayNetSales.Text = todayTransactions.Text = todayRefunds.Text = "—";
            weeklySalesByDay.SetData
            (
                  "SALES VALUE BY DAY — THIS WEEK"
                , new[] { ("Monday", 0m), ("Tuesday", 0m), ("Wednesday", 0m), ("Thursday", 0m), ("Friday", 0m) }
                , "C0"
            );
            todayItemsByValue.SetData("ITEMS SOLD — VALUE", [], "C0");
            todayDashboardStatus.Text = "Dashboard temporarily unavailable";
        }
    }

    private static IReadOnlyList<(string Label, decimal Value)> DashboardPieBuckets(IEnumerable<(string Label, decimal Value)> source)
    {
        var ordered = source.Where(x => x.Value > 0m).OrderByDescending(x => x.Value).ToArray();
        return ordered.Length <= 6 ? ordered : ordered.Take(5).Append(("Other", ordered.Skip(5).Sum(x => x.Value))).ToArray();
    }

    private static string Money(decimal value) => value.ToString("C2");
    private static TextBlock DashboardValue() => new()
    {
        Text = "—",
        FontSize = 24,
        FontWeight = FontWeight.Bold,
        Foreground = new SolidColorBrush(Color.Parse("#182D44"))
    };

    private static TextBlock ValueText(bool emphasized = false) => new()
    {
        Text = "$0.00",
        HorizontalAlignment = HorizontalAlignment.Right,
        FontSize = emphasized ? 19 : 15,
        FontWeight = emphasized ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal
    };

    private static void AddFinancialRow(Grid grid, int row, string caption, Control value)
    {
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var label = new TextBlock { Text = caption, Margin = new Thickness(0, 2) };
        Grid.SetRow(label, row);
        grid.Children.Add(label);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);
    }

    private sealed record ProductRow(ProductCatalogItem Product)
    {
        public override string ToString() => $"{Product.Name} - {Money(Product.Price)}";
    }

    private sealed record SearchRow(CustomerSearchCandidate Customer)
    {
        public override string ToString() => Customer.Name;
    }

    private sealed record TransactionRow(CustomerMonthlyTransactionView Transaction)
    {
        public override string ToString() => $"{Transaction.OccurredAt.LocalDateTime:g} | {Transaction.Kind} | {Transaction.Description} | {Money(Transaction.Amount)} | Account {Money(Transaction.CafeteriaAccountAmount)} | Cash {Money(Transaction.CashAmount)}";
    }

    private sealed class CartRow(int productId, string productName, decimal unitPrice, int quantity)
    {
        public int ProductId { get; } = productId;
        public string ProductName { get; } = productName;
        public decimal UnitPrice { get; } = unitPrice;
        public int Quantity { get; set; } = quantity;
        public decimal Total => UnitPrice * Quantity;
    }

    private sealed record CartDisplayRow(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Total)
    {
        public override string ToString() => $"{Quantity} x {ProductName} @ {Money(UnitPrice)} = {Money(Total)}";
    }
}
