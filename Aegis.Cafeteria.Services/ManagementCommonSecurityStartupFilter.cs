using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Aegis.Cafeteria.Services;

/// <summary>
/// Inserts the Common.Security management authentication endpoint middleware into the Cafeteria Services pipeline.
/// </summary>
public sealed class ManagementCommonSecurityStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.UseManagementCommonSecurityAuthentication();
            next(application);
        };
    }
}
