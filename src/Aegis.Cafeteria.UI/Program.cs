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

        using var splash = new StartupSplashController();
        splash.Update("Loading application configuration...", "Preparing the cafeteria terminal.");

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.ServicesOnly.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables();

        splash.Update("Loading secure configuration...", "Resolving Cafeteria Services settings.");
        var secretProvider = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
        var secretSource = ApplicationSecretProviderFactory.Describe(builder.Environment);
        CafeteriaRuntimeOptions runtime;
        try
        {
            runtime = await CafeteriaRuntimeOptionsResolver.ResolveAsync(builder.Configuration, secretProvider);
            if (!runtime.Services.Enabled)
                throw new InvalidOperationException("Aegis.Cafeteria POS requires Aegis.Cafeteria.Services.");
            if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(runtime.Services.ApiKey))
                throw new InvalidOperationException("CafeteriaServicesApiKey is not configured.");
        }
        catch (Exception ex)
        {
            splash.Close();
            MessageBox.Show(
                $"Cafeteria could not load its required secrets from {secretSource}.\n\n" +
                (builder.Environment.IsDevelopment()
                    ? "Set required development secrets in appsettings.Development.json or environment variables.\n\n"
                    : "Check bws.exe, BWS_ACCESS_TOKEN, the configured secret IDs, and network access.\n\n") +
                ex.Message,
                "Secret configuration startup failure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

            var securityOptions = builder.Configuration.GetSection("Security").Get<OperatorSecurityOptions>()
                ?? new OperatorSecurityOptions();
            var uiOptions = builder.Configuration.GetSection("Ui").Get<CafeteriaUiOptions>()
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
            builder.Services.AddSingleton(sp => new CustomerPaymentWorkflow(
                sp.GetRequiredService<ICafeteriaPersistenceStore>(),
                sp.GetService<IExternalLedgerProjectionService>()));
            builder.Services.AddSingleton<MainForm>();

            splash.Update("Building terminal services...", "Initializing the POS service container.");
            using var host = builder.Build();

            splash.Update("Opening local database...", "Checking the local cache and offline transaction store.");
            await host.Services.GetRequiredService<PersistenceInitializer>().InitializeLocalAsync();

            if (runtime.FfpIntegration.Enabled)
            {
                try
                {
                    var refresh = host.Services.GetRequiredService<ReferenceDataRefreshService>();

                    splash.Update("Refreshing employees...", "Loading the latest employee reference data through Aegis.Cafeteria.Services.");
                    var count = await refresh.RefreshCustomersAsync();

                    splash.Update("Refreshing holidays...", "Loading the cafeteria working-day calendar through Aegis.Cafeteria.Services.");
                    var today = DateOnly.FromDateTime(DateTime.Today);
                    await refresh.RefreshHolidaysAsync(today);

                    if (count == 0)
                    {
                        splash.Update(
                            "Employee refresh returned no rows.",
                            "The POS will continue, but employee selection depends on the existing local cache.");
                    }
                }
                catch (Exception ex)
                {
                    splash.Update(
                        "Cafeteria Services refresh unavailable - using local cache.",
                        "The terminal will continue with reference data already stored locally.");

                    MessageBox.Show(
                        "Cafeteria could not fully refresh its workforce reference data through Aegis.Cafeteria.Services.\n\n" +
                        $"Services endpoint: {runtime.Services.BaseUrl}\n\n" + ex.Message +
                        "\n\nThe POS will continue with data already cached locally.",
                        "Reference refresh warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            splash.Update("Starting terminal services...", "Starting Services publication, health monitoring, and background services.");
            await host.StartAsync();

            splash.Update("Operator sign-in required...", "Authenticating against FFTPJ Active Directory over LDAPS.");
            splash.Close();

            using (var login = new OperatorLoginForm(
                host.Services.GetRequiredService<IOperatorAuthenticator>(),
                host.Services.GetRequiredService<IOperatorSession>()))
            {
                if (login.ShowDialog() != DialogResult.OK)
                {
                    await host.StopAsync();
                    return;
                }
            }

            var mainForm = host.Services.GetRequiredService<MainForm>();
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
            MessageBox.Show(
                ex.ToString(),
                "Cafeteria startup failure",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
