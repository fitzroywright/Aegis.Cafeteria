namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Bridges the existing AdditionalCreditService authorization abstraction to the authenticated
/// Windows/AD management principal. The supplied userId must be the same identity as the caller
/// and the caller must belong to one of the configured management groups.
/// </summary>
public sealed class AuthenticatedAdditionalCreditAuthorizer(
    IHttpContextAccessor httpContextAccessor,
    ManagementApiOptions options) : IAdditionalCreditAuthorizer
{
    public bool CanApprove(string userId)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var identity = principal?.Identity;
        if (identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(identity.Name) || string.IsNullOrWhiteSpace(userId))
            return false;

        if (!string.Equals(Normalize(identity.Name), Normalize(userId), StringComparison.OrdinalIgnoreCase))
            return false;

        return options.AuthorizedGroups.Length == 0 || options.AuthorizedGroups.Any(principal!.IsInRole);
    }

    private static string Normalize(string value)
    {
        var text = value.Trim();
        var slash = text.LastIndexOf('\\');
        if (slash >= 0 && slash < text.Length - 1) return text[(slash + 1)..];
        var at = text.IndexOf('@');
        return at > 0 ? text[..at] : text;
    }
}
