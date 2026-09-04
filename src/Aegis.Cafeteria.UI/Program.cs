using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Logging;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Security;
using Aegis.Cafeteria.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aegis.Cafeteria.UI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        UiDisplayFormattingPolicy.ConfigureApplicationCulture();
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using StartupSplashController splash = new();
        splash.Update("Loading application configuration...", "Preparing the cafeteria terminal.");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        CafeteriaRuntimeOptions runtime;
        try
        {
            runtime = CafeteriaRuntimeOptionsResolver.ResolvePosAsync(builder.Configuration)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            splash.Close();
            MessageBox.Show
            (
                "Cafeteria could not load its local runtime configuration.\n\n" + ex.Message,
                "Local configuration startup failure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return;
        }

        try
        {
            splash.Update("Preparing local storage...", "Checking runtime folders and the local offline database.");
            RuntimePathInitializer.EnsureDirectories(runtime);
            builder.Logging.AddCafeteriaLogging(runtime.Logging, runtime.Persistence.TerminalId);
            builder.Services.AddCafeteriaPos(runtime);
            builder.Services.AddCafeteriaReporting();
            builder.Services.AddSingleton<IEmployeeParticipationQuery, EfEmployeeParticipationQuery>();

            OperatorSecurityOptions securityOptions = builder.Configuration.GetSection("Security").Get<OperatorSecurityOptions>()
                ?? new OperatorSecurityOptions();
            CafeteriaUiOptions uiOptions = builder.Configuration.GetSection("Ui").Get<CafeteriaUiOptions>()
                ?? new CafeteriaUiOptions();
            builder.Services.AddSingleton(securityOptions);
            builder.Services.AddSingleton(uiOptions);
            builder.Services.AddSingleton<IOperatorSession, OperatorSession>();
            builder.Services.AddSingleton<IManagerAuthorizationRegistry, ManagerAuthorizationRegistry>();
            builder.Services.AddSingleton<IOperatorAuthenticator>
            (
                serviceProvider => serviceProvider.GetRequiredService<ServicesOperatorAuthenticator>()
            );
            builder.Services.AddSingleton<IRefundAuthorizer, CommonSecurityRefundAuthorizer>();
            builder.Services.AddSingleton<IOrderVoidAuthorizer, CommonSecurityOrderVoidAuthorizer>();
            builder.Services.AddSingleton<RefundService>();
            builder.Services.AddSingleton<PosVoidService>();
            builder.Services.AddSingleton
            (
                serviceProvider => new CustomerPaymentWorkflow
                (
                      serviceProvider.GetRequiredService<ICafeteriaPersistenceStore>()
                    , serviceProvider.GetService<IExternalLedgerProjectionService>()
                )
            );

            if (runtime.Services.Enabled)
            {
                builder.Services.AddSingleton<WeeklySummaryPostSaleObserver>();
                builder.Services.AddSingleton<IPostSaleObserver>
                (
                    serviceProvider => serviceProvider.GetRequiredService<WeeklySummaryPostSaleObserver>()
                );
            }

            builder.Services.AddSingleton<MainForm>();

            splash.Update("Building terminal services...", "Initializing the local POS runtime.");
            using IHost host = builder.Build();

            splash.Update("Opening local database...", "Checking the local cache and offline transaction store.");
            host.Services.GetRequiredService<PersistenceInitializer>()
                .InitializeLocalAsync()
                .GetAwaiter()
                .GetResult();

            splash.Update("Operator sign-in required...", "Sign in to continue through Aegis.Cafeteria.Services and Common.Security.");
            splash.Hide();

            if (!AuthenticateOperator(host, splash))
            {
                splash.Close();
                return;
            }

            splash.Show();
            splash.Update
            (
                  "Connected to Cafeteria Services..."
                , "Operator authenticated. Refreshing cafeteria reference data."
            );

            RefreshStartupReferenceData(host, runtime, splash);

            splash.Update
            (
                  "Starting terminal services..."
                , "Starting synchronization, transaction publishing, and realtime updates."
            );
            host.StartAsync().GetAwaiter().GetResult();

            splash.Ready("Cafeteria Services is connected and the terminal is ready.");
            Thread.Sleep(250);
            splash.Close();

            try
            {
                RunTerminalUi(host, uiOptions);
            }
            finally
            {
                host.StopAsync().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            splash.Close();
            MessageBox.Show
            (
                ex.ToString(),
                "Cafeteria startup failure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private static bool AuthenticateOperator(IHost host, StartupSplashController splash)
    {
        using OperatorLoginForm login = new
        (
              host.Services.GetRequiredService<IOperatorAuthenticator>()
            , host.Services.GetRequiredService<IOperatorSession>()
            , (status, detail) =>
            {
                splash.Update(status, detail);
                splash.Show();
            }
            , splash.Hide
        );
        return login.ShowDialog() == DialogResult.OK;
    }

    private static void RefreshStartupReferenceData
    (
          IHost host
        , CafeteriaRuntimeOptions runtime
        , StartupSplashController splash
    )
    {
        if (!runtime.FfpIntegration.Enabled || !runtime.Services.Enabled)
        {
            return;
        }

        TimeSpan timeout = TimeSpan.FromSeconds(runtime.Services.StartupRefreshTimeoutSeconds);
        using CancellationTokenSource cancellation = new(timeout);

        try
        {
            ReferenceDataRefreshService refresh = host.Services.GetRequiredService<ReferenceDataRefreshService>();

            splash.Update
            (
                  "Refreshing employees..."
                , "Loading current employee and cafeteria eligibility data from Cafeteria Services."
            );
            refresh.RefreshCustomersAsync(cancellation.Token)
                .GetAwaiter()
                .GetResult();

            splash.Update
            (
                  "Refreshing holidays..."
                , "Loading the current working-day calendar from Cafeteria Services."
            );
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            refresh.RefreshHolidaysAsync(today, cancellation.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Aegis.Cafeteria.Startup");
            logger.LogWarning
            (
                  "Cafeteria Services startup refresh exceeded {TimeoutSeconds} seconds. Continuing with the local cache."
                , runtime.Services.StartupRefreshTimeoutSeconds
            );

            splash.Update
            (
                  "Cafeteria Services is responding slowly..."
                , "Starting with the local cache; background synchronization will continue."
            );
        }
        catch (Exception exception)
        {
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Aegis.Cafeteria.Startup");
            logger.LogWarning
            (
                  exception
                , "Cafeteria Services startup reference refresh failed. Continuing with the local cache."
            );

            splash.Update
            (
                  "Reference refresh unavailable..."
                , "Starting with the local cache; background synchronization will retry."
            );
        }
    }

    private static void RunTerminalUi(IHost host, CafeteriaUiOptions uiOptions)
    {
        using WinFormsBusySynchronizationContext busyContext = WinFormsBusySynchronizationContext.Install();
        MainForm mainForm = host.Services.GetRequiredService<MainForm>();
        PosCashierUiPolicy.Apply
        (
              mainForm
            , host.Services.GetRequiredService<IOrderQuery>()
            , host.Services.GetRequiredService<ICustomerIdentificationService>()
        );
        FfpDataToolsUiPolicy.Apply(uiOptions);
        HomeDashboardEnhancementPolicy.Apply
        (
              mainForm
            , host.Services.GetRequiredService<IEmployeeParticipationQuery>()
            , host.Services.GetRequiredService<ICafeteriaReportService>()
        );

        EventHandler applyFormatting = (_, _) => UiDisplayFormattingPolicy.ApplyToOpenForms();
        System.Windows.Forms.Application.Idle += applyFormatting;
        try
        {
            UiDisplayFormattingPolicy.ApplyToOpenForms();
            System.Windows.Forms.Application.Run(mainForm);
        }
        finally
        {
            System.Windows.Forms.Application.Idle -= applyFormatting;
        }
    }
}
