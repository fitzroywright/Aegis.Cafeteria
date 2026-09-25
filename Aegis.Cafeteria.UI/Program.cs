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
using Aegis.Cafeteria.Infrastructure.Registration;
using Aegis.Cafeteria.Infrastructure.Security;
using Aegis.Cafeteria.Infrastructure.Secrets;
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
            IApplicationSecretProvider startupSecrets = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
            runtime = CafeteriaRuntimeOptionsResolver.ResolvePosAsync(builder.Configuration, startupSecrets)
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

            RegisterControlPlaneModules(builder, runtime, "Windows");
            builder.Services.AddSingleton<IEmployeeParticipationQuery, EfEmployeeParticipationQuery>();

            OperatorSecurityOptions securityOptions = builder.Configuration.GetSection("Security").Get<OperatorSecurityOptions>()
                ?? new OperatorSecurityOptions();
            CafeteriaUiOptions uiOptions = builder.Configuration.GetSection("Ui").Get<CafeteriaUiOptions>()
                ?? new CafeteriaUiOptions();
            builder.Services.AddCafeteriaOperatorAuthentication(securityOptions);
            builder.Services.AddSingleton(uiOptions);
            builder.Services.AddSingleton<IOperatorSession, OperatorSession>();
            builder.Services.AddSingleton<IManagerAuthorizationRegistry, ManagerAuthorizationRegistry>();
            builder.Services.AddSingleton<IRefundAuthorizer, CommonSecurityRefundAuthorizer>();
            builder.Services.AddSingleton<IOrderVoidAuthorizer, CommonSecurityOrderVoidAuthorizer>();
            builder.Services.AddSingleton<RefundService>();
            builder.Services.AddSingleton<PosVoidService>();
            builder.Services.AddSingleton
            (
                serviceProvider => new CustomerPaymentWorkflow
                (
                      serviceProvider.GetRequiredService<ICafeteriaPersistenceStore>()
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

            splash.Update("Operator sign-in required...", "Sign in through Common.Security.");
            splash.Hide();

            if (!AuthenticateOperator(host, splash))
            {
                splash.Close();
                return;
            }

            splash.Ready("Cafeteria is ready.");
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

    private static void RegisterControlPlaneModules(
        HostApplicationBuilder builder,
        CafeteriaRuntimeOptions runtime,
        string platform)
    {
        string instanceId = CafeteriaRegistrationIdentity.BuildInstanceId(
            runtime.Persistence.TerminalId,
            platform);
        string? siteId = builder.Configuration["Deployment:SiteId"];

        builder.Services.AddCafeteriaControlPlaneRegistration(
            new CafeteriaControlPlaneRegistrationOptions(
                "Aegis.Cafeteria.POS",
                "Aegis Cafeteria POS",
                "Aegis.Cafeteria.POS",
                instanceId,
                CafeteriaRegistrationIdentity.BuildIdentityFilePath(
                    "Aegis.Cafeteria.POS",
                    instanceId),
                platform,
                siteId));

        builder.Services.AddCafeteriaControlPlaneRegistration(
            new CafeteriaControlPlaneRegistrationOptions(
                "Aegis.Cafeteria.POS.UI",
                "Aegis Cafeteria POS UI",
                "Aegis.Cafeteria.POS.UI",
                instanceId,
                CafeteriaRegistrationIdentity.BuildIdentityFilePath(
                    "Aegis.Cafeteria.POS.UI",
                    instanceId),
                platform,
                siteId));
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

    private static async Task StartBackgroundServicesAsync(IHost host, Form owner)
    {
        try
        {
            await host.StartAsync();
        }
        catch (Exception exception)
        {
            try
            {
                ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Aegis.Cafeteria.BackgroundStartup");
                logger.LogWarning(
                    exception,
                    "Background service startup failed after the POS became interactive. Local POS operation remains available.");
            }
            catch
            {
            }

            if (!owner.IsDisposed)
            {
                owner.BeginInvoke(() =>
                {
                    // Do not block the cashier with a modal failure. Background
                    // integration will remain degraded and diagnostics/logging
                    // can surface the cause.
                });
            }
        }
    }

    private static void RunTerminalUi(IHost host, CafeteriaUiOptions uiOptions)
    {
        MainForm mainForm = host.Services.GetRequiredService<MainForm>();
        PosCashierUiPolicy.Apply
        (
              mainForm
            , host.Services.GetRequiredService<IOrderQuery>()
            , host.Services.GetRequiredService<ICustomerIdentificationService>()
        );
        HomeDashboardEnhancementPolicy.Apply
        (
              mainForm
            , host.Services.GetRequiredService<IEmployeeParticipationQuery>()
            , host.Services.GetRequiredService<ICafeteriaReportService>()
        );

        mainForm.Shown += (_, _) =>
        {
            mainForm.BeginInvoke
            (
                () =>
                {
                    if (mainForm.WindowState == FormWindowState.Minimized)
                    {
                        mainForm.WindowState = FormWindowState.Maximized;
                    }

                    mainForm.BringToFront();
                    mainForm.Activate();
                    mainForm.Focus();

                    // The till is now visible and interactive. Only now start
                    // synchronization, Control Plane registration, telemetry,
                    // SignalR and other hosted background services.
                    _ = StartBackgroundServicesAsync(host, mainForm);
                }
            );
        };

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
