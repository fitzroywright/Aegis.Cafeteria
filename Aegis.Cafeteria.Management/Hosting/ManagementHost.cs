using Microsoft.AspNetCore.Authentication.Cookies;

namespace Aegis.Cafeteria.Management;

internal static class ManagementHost
{
    internal const string AuthenticationScheme = "AegisManagement";
    internal const string SessionClaim = "aegis_management_session";

    internal static void Configure(WebApplicationBuilder builder)
    {
        int configuredMinutes = builder.Configuration.GetValue<int?>("Authentication:SessionMinutes") ?? 30;
        TimeSpan lifetime = TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 5, 480));
        builder.Services.AddSingleton(new ManagementSessionStore(lifetime));
        builder.Services
            .AddAuthentication(AuthenticationScheme)
            .AddCookie(AuthenticationScheme, options => ConfigureCookie(options, lifetime));
        builder.Services.AddAuthorization();
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, TimeSpan lifetime)
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Cookie.Name = "Aegis.Cafeteria.Management";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = lifetime;
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context => RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => RedirectOrStatus(context, StatusCodes.Status403Forbidden);
    }

    private static Task RedirectOrStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}