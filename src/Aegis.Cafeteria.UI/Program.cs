using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Logging;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Secrets;
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
    private static async Task Main()
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        using StartupSplashController splash = new();
        splash.Update("Loading application configuration...", "Preparing the cafeteria terminal.");

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        splash.Update("Loading secure configuration...", "Preparing online integration settings. Offline startup remains available.");

        IApplicationSecretProvider configuredSecretProvider;
        try
        {
            configuredSecretProvider = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
        }
        catch
        {
            configuredSecretProvider = new NullApplicationSecretProvider();
        }

        IApplicationSecretProvider secretProvider = builder.Environment.IsDevelopment()
            ? configuredSecretProvider
            : new BestEffortApplicationSecretProvider(configuredSecretProvider, TimeSpan.FromSeconds(3));

        CafeteriaRuntimeOptions runtime;
        try
        {
            runtime = await CafeteriaRuntimeOptionsResolver.ResolveAsync
            (
                  builder.Configuration
                , secretProvider
                , allowDirectFfpDatabase: false
            );
        }
        catch (Exception ex)
        {
            splash.Close();
            MessageBox.Show
            (
                "Cafeteria could not load its local runtime configuration.\n\n" +
                "Online services and remote secrets are optional for POS startup, so this error indicates a local configuration problem.\n\n" +
                ex.Message,
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
            builder.Services.AddSingleton<IApplicationSecretProvider>(secretProvider);
            builder.Services.AddCafeteriaPos(runtime);
            builder.Services.AddCafeteriaReporting();

            OperatorSecurityOptions securityOptions = builder.Configuration.GetSection("Security").Get<OperatorSecurityOptions>()
                ?? new OperatorSecurityOptions();
            CafeteriaUiOptions uiOptions = builder.Configuration.GetSection("Ui").Get<CafeteriaUiOptions>()
                ?? new CafeteriaUiOptions();
            builder.Services.AddSingleton(securityOptions);
            builder.Services.AddSingleton(uiOptions);
            builder.Services.AddSingleton<IOperatorSession, OperatorSession>();
            builder.Services.AddSingleton<IManagerAuthorizationRegistry, ManagerAuthorizationRegistry>();
            builder.Services.AddSingleton<IOperatorAuthenticator, CommonSecurityOperatorAuthenticator>();
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
            builder.Services.AddSingleton<MainForm>();

            splash.Update("Building terminal services...", "Initializing local POS services and optional online integrations.");
            using IHost host = builder.Build();

            splash.Update("Opening local database...", "Checking the local cache and offline transaction store.");
            await host.Services.GetRequiredService<PersistenceInitializer>().InitializeLocalAsync();

            if (runtime.FfpIntegration.Enabled && runtime.Services.Enabled)
            {
                await TryRefreshStartupReferenceDataAsync(host, runtime, splash);
            }
            else if (runtime.FfpIntegration.Enabled)
            {
                splash.Update
                (
                    "Cafeteria Services disabled - offline mode.",
                    "Using the local cache and durable local transaction outbox."
                );
            }

            splash.Update("Starting terminal services...", "Starting local health monitoring and available background integrations.");
            await host.StartAsync();

            splash.Update("Operator sign-in required...", "Authenticating against FFTPJ Active Directory over LDAPS.");
            splash.Close();

            using WinFormsBusySynchronizationContext busyContext = WinFormsBusySynchronizationContext.Install();
            using (OperatorLoginForm login = new
            (
                  host.Services.GetRequiredService<IOperatorAuthenticator>()
                , host.Services.GetRequiredService<IOperatorSession>()
            ))
            {
                if (login.ShowDialog() != DialogResult.OK)
                {
                    await host.StopAsync();
                    return;
                }
            }

            MainForm mainForm = host.Services.GetRequiredService<MainForm>();
            PosCashierUiPolicy.Apply(mainForm);
            try
            {
                System.Windows.Forms.Application.Run(mainForm);
            }
            finally
            {
                await host.StopAsync();
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

    private static async Task TryRefreshStartupReferenceDataAsync
    (
          IHost host
        , CafeteriaRuntimeOptions runtime
        , StartupSplashController splash
    )
    {
        TimeSpan startupRefreshTimeout = TimeSpan.FromSeconds(runtime.Services.StartupRefreshTimeoutSeconds);
        using CancellationTokenSource startupRefresh = new(startupRefreshTimeout);

        try
        {
            ReferenceDataRefreshService refresh = host.Services.GetRequiredService<ReferenceDataRefreshService>();

            splash.Update("Refreshing employees...", "Checking for current employee data. Startup will continue offline if Services is slow.");
            int count = await refresh.RefreshCustomersAsync(startupRefresh.Token);

            splash.Update("Refreshing holidays...", "Checking the working-day calendar. Startup will continue offline if Services is slow.");
            DateOnly today = DateOnly.FromDateTime(DateTime.Today);
            await refresh.RefreshHolidaysAsync(today, startupRefresh.Token);

            if (count == 0)
            {
                splash.Update
                (
                    "Employee refresh returned no rows.",
                    "The POS will continue using the existing local employee cache."
                );
            }
        }
        catch (OperationCanceledException) when (startupRefresh.IsCancellationRequested)
        {
            splash.Update
            (
                $"Online refresh exceeded {runtime.Services.StartupRefreshTimeoutSeconds} seconds - starting from local cache.",
                "Cafeteria Services will synchronize again after the terminal has started."
            );
        }
        catch (Exception ex)
        {
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Aegis.Cafeteria.Startup");
            logger.LogWarning
            (
                  ex
                , "Cafeteria Services unavailable during startup reference refresh. Endpoint: {Endpoint}. Continuing from local cache."
                , runtime.Services.BaseUrl
            );

            splash.Update
            (
                "Cafeteria Services unavailable - using local cache.",
                "The terminal is continuing in offline mode."
            );
        }
    }
}
