namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Application.Abstractions;

using Microsoft.AspNetCore.Http;

public sealed class AuthenticatedStipendAuthorizer
    : IStipendAuthorizer
{
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly ManagementApiOptions options;

    public AuthenticatedStipendAuthorizer
    (
          IHttpContextAccessor httpContextAccessor
        , ManagementApiOptions options
    )
    {
        this.httpContextAccessor = httpContextAccessor;
        this.options = options;
    }

    public bool CanProcessStipend(string userId)
    {
        System.Security.Claims.ClaimsPrincipal? principal = this.httpContextAccessor.HttpContext?.User;
        System.Security.Principal.IIdentity? identity = principal?.Identity;

        if (identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(identity.Name)
            || string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        if (!string.Equals(Normalize(identity.Name), Normalize(userId), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return this.options.AuthorizedGroups.Length == 0
            || this.options.AuthorizedGroups.Any(principal!.IsInRole);
    }

    private static string Normalize(string value)
    {
        string text = value.Trim();
        int slash = text.LastIndexOf('\\');

        if (slash >= 0 && slash < text.Length - 1)
        {
            return text[(slash + 1)..];
        }

        int at = text.IndexOf('@');
        return at > 0 ? text[..at] : text;
    }
}
