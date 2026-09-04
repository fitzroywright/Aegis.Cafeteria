using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Security;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Infrastructure;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Logging;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Secrets;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aegis.Cafeteria.UI.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        LinuxUiParityPolicy.ConfigureApplicationCulture();

        var builder = Host.CreateApplicationBuilder(args);
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        var secretProvider = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
        var runtime = CafeteriaRuntimeOptionsResolver
            .ResolvePosAsync(builder.Configuration, secretProvider)
            .GetAwaiter().GetResult();
        if (!builder.Environment.IsDevelopment() && runtime.Services.Enabled && string.IsNullOrWhiteSpace(runtime.Services.ApiKey))
            throw new InvalidOperationException("Cafeteria Services is enabled but CafeteriaServicesApiKey is not configured.");

        RuntimePathInitializer.EnsureDirectories(runtime);
        builder.Logging.AddCafeteriaLogging(runtime.Logging, runtime.Persistence.TerminalId);
        builder.Services.AddSingleton<IApplicationSecretProvider>(secretProvider);
        builder.Services.AddCafeteriaPos(runtime);
        builder.Services.AddCafeteriaReporting();
        var securityOptions = builder.Configuration.GetSection("Security").Get<OperatorSecurityOptions>() ?? new OperatorSecurityOptions();
        builder.Services.AddSingleton(securityOptions);
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
        if (runtime.Services.Enabled)
        {
            builder.Services.AddSingleton<WeeklySummaryPostSaleObserver>();
            builder.Services.AddSingleton<IPostSaleObserver>(sp => sp.GetRequiredService<WeeklySummaryPostSaleObserver>());
        }
        var uiOptions = builder.Configuration.GetSection("Ui").Get<CafeteriaUiOptions>() ?? new CafeteriaUiOptions();
        builder.Services.AddSingleton(uiOptions);
        builder.Services.AddSingleton<MainWindow>();

        using var host = builder.Build();
        host.Services.GetRequiredService<PersistenceInitializer>()
            .InitializeLocalAsync().GetAwaiter().GetResult();
        if (runtime.FfpIntegration.Enabled && runtime.Services.Enabled)
        {
            try
            {
                var refresh = host.Services.GetRequiredService<ReferenceDataRefreshService>();
                refresh.RefreshCustomersAsync().GetAwaiter().GetResult();
                var today = DateOnly.FromDateTime(DateTime.Today);
                refresh.RefreshHolidaysAsync(today).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Workforce reference startup refresh failed: {ex.GetBaseException().Message}");
            }
        }
        host.StartAsync().GetAwaiter().GetResult();

        try
        {
            var lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                Args = args,
                ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose
            };

            AppBuilder.Configure<Avalonia.Application>()
                .UsePlatformDetect()
                .AfterSetup(appBuilder => appBuilder.Instance?.Styles.Add(new FluentTheme()))
                .SetupWithLifetime(lifetime);

            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
            LinuxUiParityPolicy.Apply(mainWindow);
            lifetime.MainWindow = mainWindow;
            lifetime.Start(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
        }
    }
}
