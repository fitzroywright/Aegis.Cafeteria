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

            splash.Update("Operator sign-in required...", "Authenticating through Aegis.Cafeteria.Services and Common.Security.");
            splash.Close();

            if (!AuthenticateOperator(host))
            {
                return;
            }

            host.StartAsync().GetAwaiter().GetResult();
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

    private static bool AuthenticateOperator(IHost host)
    {
        using OperatorLoginForm login = new
        (
              host.Services.GetRequiredService<IOperatorAuthenticator>()
            , host.Services.GetRequiredService<IOperatorSession>()
        );
        return login.ShowDialog() == DialogResult.OK;
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
