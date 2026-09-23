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

namespace Aegis.Cafeteria.UI.Linux;

internal static class LinuxCafeteriaHost
{
    public static IHost Build(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        ConfigureSources(builder);
        CafeteriaRuntimeOptions runtime = ResolveRuntime(builder);
        ConfigureServices(builder, runtime);
        return builder.Build();
    }

    private static void ConfigureSources(HostApplicationBuilder builder)
    {
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    }

    private static CafeteriaRuntimeOptions ResolveRuntime(HostApplicationBuilder builder)
    {
        IApplicationSecretProvider startupSecrets = ApplicationSecretProviderFactory.Create(builder.Configuration);
        CafeteriaRuntimeOptions runtime = CafeteriaRuntimeOptionsResolver
            .ResolvePosAsync(
                builder.Configuration,
                startupSecrets,
                "Aegis.Cafeteria.LinuxUI",
                "Aegis Cafeteria Linux UI")
            .GetAwaiter()
            .GetResult();
        RuntimePathInitializer.EnsureDirectories(runtime);
        return runtime;
    }

    private static void ConfigureServices(HostApplicationBuilder builder, CafeteriaRuntimeOptions runtime)
    {
        builder.Logging.AddCafeteriaLogging(runtime.Logging, runtime.Persistence.TerminalId);
        builder.Services.AddCafeteriaPos(runtime);
        builder.Services.AddCafeteriaReporting();

        OperatorSecurityOptions securityOptions = builder.Configuration.GetSection("Security").Get<OperatorSecurityOptions>() ?? new();
        CafeteriaUiOptions uiOptions = builder.Configuration.GetSection("Ui").Get<CafeteriaUiOptions>() ?? new();
        builder.Services.AddSingleton(securityOptions);
        builder.Services.AddSingleton(uiOptions);
        builder.Services.AddSingleton<IOperatorSession, OperatorSession>();
        builder.Services.AddSingleton<IManagerAuthorizationRegistry, ManagerAuthorizationRegistry>();
        builder.Services.AddSingleton<IOperatorAuthenticator>(sp => sp.GetRequiredService<ServicesOperatorAuthenticator>());
        builder.Services.AddSingleton<IRefundAuthorizer, CommonSecurityRefundAuthorizer>();
        builder.Services.AddSingleton<IOrderVoidAuthorizer, CommonSecurityOrderVoidAuthorizer>();
        builder.Services.AddSingleton<RefundService>();
        builder.Services.AddSingleton<PosVoidService>();
        builder.Services.AddSingleton(sp => new CustomerPaymentWorkflow(
            sp.GetRequiredService<ICafeteriaPersistenceStore>()));

        if (runtime.Services.Enabled)
        {
            builder.Services.AddSingleton<WeeklySummaryPostSaleObserver>();
            builder.Services.AddSingleton<IPostSaleObserver>(sp => sp.GetRequiredService<WeeklySummaryPostSaleObserver>());
        }

        builder.Services.AddSingleton<MainWindow>();
    }
}
