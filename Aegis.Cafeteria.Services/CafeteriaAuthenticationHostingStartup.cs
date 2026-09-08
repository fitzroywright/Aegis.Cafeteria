using Aegis.Cafeteria.Services;
using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using Common.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(CafeteriaAuthenticationHostingStartup))]

namespace Aegis.Cafeteria.Services;

public sealed class CafeteriaAuthenticationHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices
        (
            services =>
            {
                services.AddSingleton<ManagementCommonSecurityAuthenticator>();
                services.AddSingleton<ManagementServiceTokenStore>();
                services.AddSingleton<CafeteriaRoleResolver>();
                services.AddSingleton<IStartupFilter, ManagementCommonSecurityStartupFilter>();
                services.AddSingleton<IStartupFilter, CafeteriaPermissionStartupFilter>();
                services.AddSingleton<IStartupFilter, CafeteriaRuntimeStartupFilter>();

                services.AddAuthorization(options =>
                {
                    options.AddPolicy(CafeteriaPolicies.Supervisor, policy =>
                        policy.RequireAssertion(context => CafeteriaRoleResolver.HasRole(context.User,
                            CafeteriaRoles.Supervisor, CafeteriaRoles.Manager, CafeteriaRoles.Administrator)));
                    options.AddPolicy(CafeteriaPolicies.Manager, policy =>
                        policy.RequireAssertion(context => CafeteriaRoleResolver.HasRole(context.User,
                            CafeteriaRoles.Manager, CafeteriaRoles.Administrator)));
                    options.AddPolicy(CafeteriaPolicies.Administrator, policy =>
                        policy.RequireAssertion(context => CafeteriaRoleResolver.HasRole(context.User,
                            CafeteriaRoles.Administrator)));
                    options.AddPolicy(CafeteriaPolicies.Audit, policy =>
                        policy.RequireAssertion(context => CafeteriaPermissions.HasPermission(context.User, CafeteriaPermissions.ViewAudit)));

                    foreach (string permission in CafeteriaPermissions.All)
                    {
                        string captured = permission;
                        options.AddPolicy(CafeteriaPolicies.ForPermission(captured), policy =>
                            policy.RequireAssertion(context => CafeteriaPermissions.HasPermission(context.User, captured)));
                    }
                });

                services.AddCommonDiagnostics();
                services.AddDiagnosticCheck<CafeteriaDatabaseDiagnostic>();
                services.AddDiagnosticCheck<CafeteriaSecretsDiagnostic>();
                services.AddDiagnosticCheck<CafeteriaStorageDiagnostic>();
                services.AddDiagnosticCheck<CafeteriaMessagingDiagnostic>();
                services.AddDiagnosticCheck<CafeteriaActiveDirectoryDiagnostic>();
                services.AddHostedService<CafeteriaDependencyDiagnosticHostedService>();
            }
        );
    }
}
