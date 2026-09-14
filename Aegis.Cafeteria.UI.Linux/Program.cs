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

namespace Aegis.Cafeteria.UI.Linux;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        LinuxUiParityPolicy.ConfigureApplicationCulture();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        IApplicationSecretProvider startupSecrets = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
        CafeteriaRuntimeOptions runtime = CafeteriaRuntimeOptionsResolver
            .ResolvePosAsync(builder.Configuration, startupSecrets)
            .GetAwaiter()
            .GetResult();

        RuntimePathInitializer.EnsureDirectories(runtime);
        builder.Logging.AddCafeteriaLogging(runtime.Logging, runtime.Persistence.TerminalId);
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

        builder.Services.AddSingleton<MainWindow>();

        using IHost host = builder.Build();
        host.Services.GetRequiredService<PersistenceInitializer>()
            .InitializeLocalAsync()
            .GetAwaiter()
            .GetResult();

        ClassicDesktopStyleApplicationLifetime lifetime = new()
        {
            Args = args,
            ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose
        };

        AppBuilder.Configure<Avalonia.Application>()
            .UsePlatformDetect()
            .AfterSetup(appBuilder => appBuilder.Instance?.Styles.Add(new FluentTheme()))
            .SetupWithLifetime(lifetime);

        IOperatorSession operatorSession = host.Services.GetRequiredService<IOperatorSession>();
        OperatorLoginWindow login = new
        (
              host.Services.GetRequiredService<IOperatorAuthenticator>()
            , operatorSession
        );

        login.Closed += (_, _) =>
        {
            if (!operatorSession.IsAuthenticated)
            {
                lifetime.Shutdown();
                return;
            }

            try
            {
                host.StartAsync().GetAwaiter().GetResult();
                MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
                LinuxUiParityPolicy.Apply(mainWindow);
                lifetime.MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Cafeteria startup failed after sign-in: {exception.GetBaseException().Message}");
                lifetime.Shutdown();
            }
        };

        lifetime.MainWindow = login;

        try
        {
            lifetime.Start(args);
        }
        finally
        {
            if (operatorSession.IsAuthenticated)
            {
                host.StopAsync().GetAwaiter().GetResult();
            }
        }
    }
}
