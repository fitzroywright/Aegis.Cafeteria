using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Cafeteria.UI.Linux;

internal static class LinuxDesktopRunner
{
    public static void Run(IHost host, string[] args)
    {
        InitializePersistence(host);
        ClassicDesktopStyleApplicationLifetime lifetime = CreateLifetime(args);
        ConfigureAvalonia(lifetime);
        IOperatorSession operatorSession = host.Services.GetRequiredService<IOperatorSession>();
        OperatorLoginWindow login = CreateLogin(host, operatorSession, lifetime);
        lifetime.MainWindow = login;

        try
        {
            lifetime.Start(args);
        }
        finally
        {
            StopHostIfStarted(host, operatorSession);
        }
    }

    private static void InitializePersistence(IHost host) =>
        host.Services.GetRequiredService<PersistenceInitializer>()
            .InitializeLocalAsync()
            .GetAwaiter()
            .GetResult();

    private static ClassicDesktopStyleApplicationLifetime CreateLifetime(string[] args) => new()
    {
        Args = args,
        ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose
    };

    private static void ConfigureAvalonia(ClassicDesktopStyleApplicationLifetime lifetime) =>
        AppBuilder.Configure<Avalonia.Application>()
            .UsePlatformDetect()
            .AfterSetup(appBuilder => appBuilder.Instance?.Styles.Add(new FluentTheme()))
            .SetupWithLifetime(lifetime);

    private static OperatorLoginWindow CreateLogin(IHost host, IOperatorSession operatorSession, ClassicDesktopStyleApplicationLifetime lifetime)
    {
        OperatorLoginWindow login = new(host.Services.GetRequiredService<IOperatorAuthenticator>(), operatorSession);
        login.Closed += (_, _) => HandleLoginClosed(host, operatorSession, lifetime);
        return login;
    }

    private static void HandleLoginClosed(IHost host, IOperatorSession operatorSession, ClassicDesktopStyleApplicationLifetime lifetime)
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
    }

    private static void StopHostIfStarted(IHost host, IOperatorSession operatorSession)
    {
        if (operatorSession.IsAuthenticated)
            host.StopAsync().GetAwaiter().GetResult();
    }
}
