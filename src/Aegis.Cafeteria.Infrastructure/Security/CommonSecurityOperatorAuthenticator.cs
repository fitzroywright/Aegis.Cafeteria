namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Common.Security.Abstractions;
using Common.Security.Authenticators;
using Common.Security.Models;
using Common.Security.Options;
using Microsoft.Extensions.Logging;

/// <summary>
/// Authenticates cafeteria operators through the shared Common.Security project.
/// Common.Security owns AD/LDAPS authentication; Aegis.Cafeteria owns the
/// application session and manager authorization policy.
/// </summary>
public sealed class CommonSecurityOperatorAuthenticator : IOperatorAuthenticator
{
    private readonly IAuthenticator authenticator;
    private readonly ILogger<CommonSecurityOperatorAuthenticator> logger;

    public CommonSecurityOperatorAuthenticator(
        OperatorSecurityOptions options,
        ILogger<CommonSecurityOperatorAuthenticator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (!options.Enabled)
            throw new InvalidOperationException("Operator security cannot be disabled for the production POS.");

        Validate(options);

        var adOptions = new ActiveDirectoryAuthenticationOptions
        {
            Domain = options.Domain,
            SearchBase = options.SearchBase,
            Port = options.Port,
            TimeoutSeconds = options.TimeoutSeconds,
            Servers = options.Servers,
            AdministrativeGroups = options.AdministrativeGroups
        };

        authenticator = new ActiveDirectory(adOptions);
        this.logger = logger;
    }

    public CommonSecurityOperatorAuthenticator(
        IAuthenticator authenticator,
        ILogger<CommonSecurityOperatorAuthenticator> logger)
    {
        this.authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<OperatorIdentity?> AuthenticateAsync(
        string loginName,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
            return Task.FromResult<OperatorIdentity?>(null);

        try
        {
            AuthenticationResult result = authenticator.Authenticate(loginName, password);

            // Fail closed. A usable directory identity is required in addition
            // to successful credential validation.
            if (!result.IsAuthenticated || result.UserInfo is null)
                return Task.FromResult<OperatorIdentity?>(null);

            string userId = Normalize(loginName);
            string displayName = string.IsNullOrWhiteSpace(result.UserInfo.DisplayName)
                ? userId
                : result.UserInfo.DisplayName.Trim();

            logger.LogInformation(
                "Cafeteria operator {UserId} authenticated. Manager={IsManager}",
                userId,
                result.IsAdministrative);

            return Task.FromResult<OperatorIdentity?>(
                new OperatorIdentity(
                    userId,
                    displayName,
                    result.IsAdministrative,
                    DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // Never log the password. Common.Security also keeps credentials
            // out of its logs.
            logger.LogWarning(
                ex,
                "Common.Security authentication failed for {LoginName}",
                Normalize(loginName));

            return Task.FromResult<OperatorIdentity?>(null);
        }
    }

    private static string Normalize(string loginName)
    {
        string value = loginName.Trim();

        int slash = value.LastIndexOf('\\');
        if (slash >= 0 && slash < value.Length - 1)
            value = value[(slash + 1)..];

        int at = value.IndexOf('@');
        if (at > 0)
            value = value[..at];

        return value.ToLowerInvariant();
    }

    private static void Validate(OperatorSecurityOptions value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value.Domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.SearchBase);

        if (value.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(value.Port));

        if (value.TimeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(value.TimeoutSeconds));

        if (value.Servers.Count == 0 || value.Servers.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(
                "At least one valid LDAPS server is required.",
                nameof(value.Servers));

        if (value.AdministrativeGroups is null)
            throw new ArgumentException(
                "AdministrativeGroups must be configured.",
                nameof(value.AdministrativeGroups));
    }
}
