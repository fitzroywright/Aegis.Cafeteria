namespace Common.Security.Authenticators
{
    using Common.Security.Abstractions;
    using Common.Security.Models;
    using Common.Security.Options;
    using Common.Security.Services.Extensions;
    using Common.Security.Services.Functions;

    using System.DirectoryServices.Protocols;
    using System.Net;

    public sealed class ActiveDirectory : IAuthenticator
    {
        private static readonly log4net.ILog logger =
            log4net.LogManager.GetLogger(typeof(ActiveDirectory));

        private static readonly string[] ADAuthenticationAttributes =
        [
            "title",
            "displayName",
            "department",
            "mail",
            "manager",
            "telephoneNumber",
            "otherTelephone",
            "mobile",
            "otherMobile",
            "thumbnailPhoto",
            "userAccountControl",
            "memberOf"
        ];

        private readonly ActiveDirectoryAuthenticationOptions options;
        private readonly string[] servers;
        private readonly string[] administrativeGroupPatterns;

        private volatile string? preferredServer;

        public ActiveDirectory(
            ActiveDirectoryAuthenticationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            ValidateOptions(options);

            this.options = options;

            servers = options.Servers
                .Where(server => !string.IsNullOrWhiteSpace(server))
                .Select(server => server.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            administrativeGroupPatterns =
                options.AdministrativeGroups
                    .Where(group => !string.IsNullOrWhiteSpace(group))
                    .Select(CreateGroupDnPattern)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        public AuthenticationResult Authenticate(string loginName, 
            string password)
        {
            if (string.IsNullOrWhiteSpace(loginName) ||
                string.IsNullOrWhiteSpace(password))
            {
                return AuthenticationFailed();
            }

            string userName =
                UserNameNormalizer.GetUserNameWithoutDomain(
                    loginName);

            if (string.IsNullOrWhiteSpace(userName))
                return AuthenticationFailed();

            using LdapConnection? connection =
                GetLdapConnection(
                    userName,
                    password);

            if (connection == null)
                return AuthenticationFailed();

            return BuildAuthenticationResult(
                connection,
                userName);
        }


        private AuthenticationResult BuildAuthenticationResult(
            LdapConnection connection,
            string userName)
        {
            SearchResultEntry? userEntry =
                SearchDirectory(
                    connection,
                    options.SearchBase,
                    userName,
                    ADAuthenticationAttributes);

            if (userEntry == null)
            {
                logger.WarnFormat(
                    "Active Directory authentication failed because the user entry could not be retrieved. UserName={0}",
                    userName);

                return AuthenticationFailed();
            }

            var userInfo =
                new ApplicationUserInfo();

            PopulateUserInfo(
                userEntry,
                userInfo);

            bool isAdministrative =
                IsAdministrative(userEntry);

            logger.DebugFormat(
                "Active Directory authentication completed. UserName={0}, IsAdministrative={1}",
                userName,
                isAdministrative);

            return new AuthenticationResult
            {
                IsAuthenticated = true,
                IsAdministrative = isAdministrative,
                UserInfo = userInfo
            };
        }

        private LdapConnection? GetLdapConnection(
            string loginName,
            string password)
        {
            string userName =
                UserNameNormalizer.GetUserNameWithoutDomain(
                    loginName);

            if (string.IsNullOrWhiteSpace(userName) ||
                string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            string userPrincipalName =
                $"{userName}@{options.Domain}";

            string? previousPreferredServer =
                preferredServer;

            foreach (string server in GetServersInPreferredOrder())
            {
                LdapConnection? connection = null;

                try
                {
                    logger.DebugFormat(
                        "Attempting Active Directory LDAPS connection. Server={0}, User={1}",
                        server,
                        userName);

                    connection =
                        CreateConnection(
                            server,
                            userPrincipalName,
                            password);

                    connection.Bind();

                    if (!string.IsNullOrWhiteSpace(
                            previousPreferredServer) &&
                        !server.Equals(
                            previousPreferredServer,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        logger.WarnFormat(
                            "Active Directory failover occurred. PreviousServer={0}, ActiveServer={1}",
                            previousPreferredServer,
                            server);
                    }

                    preferredServer = server;

                    logger.DebugFormat(
                        "Active Directory LDAPS bind succeeded. Server={0}, User={1}",
                        server,
                        userName);

                    return connection;
                }
                catch (LdapException exception)
                    when (IsInvalidCredentials(exception))
                {
                    connection?.Dispose();

                    logger.WarnFormat(
                        "Active Directory rejected credentials. Server={0}, User={1}",
                        server,
                        userName);

                    // The server successfully processed the LDAP bind
                    // and rejected the supplied credentials.
                    //
                    // Trying another domain controller will not fix
                    // invalid credentials, so fail immediately.
                    return null;
                }
                catch (LdapException exception)
                {
                    connection?.Dispose();

                    logger.Warn(
                        $"Active Directory server failed. " +
                        $"Server={server}, " +
                        $"LDAPError={exception.ErrorCode}, " +
                        $"Message={exception.Message}. " +
                        $"Trying next configured server.",
                        exception);
                }
                catch (TimeoutException exception)
                {
                    connection?.Dispose();

                    logger.Warn(
                        $"Active Directory server timed out. " +
                        $"Server={server}. " +
                        $"Trying next configured server.",
                        exception);
                }
                catch
                {
                    connection?.Dispose();

                    // Unexpected programming/runtime failures should
                    // not be converted into an authentication failure.
                    throw;
                }
            }

            logger.ErrorFormat(
                "No configured Active Directory server was available. Servers={0}",
                string.Join(", ", servers));

            return null;
        }

        private LdapConnection CreateConnection(
            string server,
            string userPrincipalName,
            string password)
        {
            var identifier =
                new LdapDirectoryIdentifier(
                    server,
                    options.Port,
                    fullyQualifiedDnsHostName: true,
                    connectionless: false);

            var credential =
                new NetworkCredential(
                    userPrincipalName,
                    password);

            var connection =
                new LdapConnection(
                    identifier,
                    credential,
                    AuthType.Basic)
                {
                    Timeout =
                        TimeSpan.FromSeconds(
                            options.TimeoutSeconds)
                };

            connection.SessionOptions.ProtocolVersion = 3;

            // LDAPS. TLS is established before the LDAP bind.
            connection.SessionOptions.SecureSocketLayer = true;

            return connection;
        }

        private IEnumerable<string> GetServersInPreferredOrder()
        {
            string? preferred =
                preferredServer;

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                string? configuredPreferred =
                    servers.FirstOrDefault(
                        server =>
                            server.Equals(
                                preferred,
                                StringComparison.OrdinalIgnoreCase));

                if (configuredPreferred != null)
                    yield return configuredPreferred;
            }

            foreach (string server in servers)
            {
                if (!server.Equals(
                        preferred,
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return server;
                }
            }
        }

        private SearchResultEntry? SearchDirectory(
            LdapConnection connection,
            string searchBase,
            string loginName,
            string[] attributes)
        {
            string userName =
                UserNameNormalizer.GetUserNameWithoutDomain(
                    loginName);

            string escapedUserName =
                LdapFilterEscaper.Escape(userName);

            try
            {
                var request =
                    new SearchRequest(
                        searchBase,
                        $"(sAMAccountName={escapedUserName})",
                        SearchScope.Subtree,
                        attributes);

                var response =
                    (SearchResponse)connection.SendRequest(
                        request);

                if (response.Entries.Count == 0)
                    return null;

                return response.Entries[0];
            }
            catch (LdapException exception)
            {
                logger.Error(
                    $"Active Directory LDAP search failed. " +
                    $"UserName={userName}, " +
                    $"SearchBase={searchBase}",
                    exception);

                return null;
            }
            catch (DirectoryOperationException exception)
            {
                logger.Error(
                    $"Active Directory directory operation failed. " +
                    $"UserName={userName}, " +
                    $"SearchBase={searchBase}",
                    exception);

                return null;
            }
        }

        private bool IsAdministrative(
            SearchResultEntry userEntry)
        {
            if (administrativeGroupPatterns.Length == 0)
                return false;

            if (!userEntry.Attributes.Contains("memberOf"))
                return false;

            DirectoryAttribute memberOfAttribute =
                userEntry.Attributes["memberOf"];

            IEnumerable<string> memberOf =
                memberOfAttribute
                    .GetValues(typeof(string))
                    .Cast<string>();

            return memberOf.Any(
                groupDn =>
                    administrativeGroupPatterns.Any(
                        pattern =>
                            groupDn.StartsWith(
                                pattern,
                                StringComparison.OrdinalIgnoreCase)));
        }

        private static void PopulateUserInfo(
            SearchResultEntry userEntry,
            ApplicationUserInfo user)
        {
            foreach (
                string attributeName
                in userEntry.Attributes.AttributeNames)
            {
                DirectoryAttribute attribute =
                    userEntry.Attributes[attributeName];

                if (attributeName.Equals(
                        "thumbnailPhoto",
                        StringComparison.OrdinalIgnoreCase))
                {
                    user.Photo =
                        attribute
                            .GetValues(typeof(byte[]))
                            .Cast<byte[]>()
                            .FirstOrDefault();

                    continue;
                }

                string? value =
                    attribute
                        .GetValues(typeof(string))
                        .Cast<string>()
                        .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                switch (attributeName.ToLowerInvariant())
                {
                    case "department":
                        user.Department = value;
                        break;

                    case "displayname":
                        user.DisplayName = value;
                        break;

                    case "mail":
                        user.Mail = value;
                        break;

                    case "manager":
                        user.Manager =
                            GetManagerCn(value)
                                .NormalizeWhitespace();
                        break;

                    case "mobile":
                        user.Mobile = value;
                        break;

                    case "othermobile":
                        user.OtherMobile = value;
                        break;

                    case "othertelephone":
                        user.OtherTelephoneNumber = value;
                        break;

                    case "telephonenumber":
                        user.TelephoneNumber = value;
                        break;

                    case "title":
                        user.Title = value;
                        break;

                    case "useraccountcontrol":
                        user.UserAccountControl =
                            long.TryParse(
                                value,
                                out long parsed)
                                ? parsed
                                : 0;

                        break;
                }
            }
        }

        private static string GetManagerCn(
            string managerValue)
        {
            if (string.IsNullOrWhiteSpace(managerValue))
                return string.Empty;

            try
            {
                return Rfc4514Parser
                    .Parse(managerValue)
                    .GetValues("CN")
                    .FirstOrDefault()
                    ?.Trim()
                    ?? string.Empty;
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static string CreateGroupDnPattern(
            string groupName)
        {
            string normalizedGroupName =
                groupName.Trim();

            // AdministrativeGroups contains group CNs rather
            // than full distinguished names.
            //
            // Example:
            // "Domain Admins"
            //
            // becomes:
            // "CN=Domain Admins,"
            return $"CN={normalizedGroupName},";
        }

        private static bool IsInvalidCredentials(
            LdapException exception)
        {
            return exception.ErrorCode == 49;
        }

        private static AuthenticationResult AuthenticationFailed()
        {
            return new AuthenticationResult
            {
                IsAuthenticated = false,
                IsAdministrative = false,
                UserInfo = null
            };
        }

        private static void ValidateOptions(
            ActiveDirectoryAuthenticationOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Domain))
            {
                throw new ArgumentException(
                    "Active Directory domain must be configured.",
                    nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.SearchBase))
            {
                throw new ArgumentException(
                    "Active Directory search base must be configured.",
                    nameof(options));
            }

            if (options.Servers is null ||
                options.Servers.Count == 0 ||
                options.Servers.All(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "At least one valid Active Directory server must be configured.",
                    nameof(options));
            }

            if (options.Port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.Port,
                    "Active Directory port must be between 1 and 65535.");
            }

            if (options.TimeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.TimeoutSeconds,
                    "Active Directory timeout must be greater than zero.");
            }

            if (options.AdministrativeGroups is null)
            {
                throw new ArgumentException(
                    "AdministrativeGroups must not be null.",
                    nameof(options));
            }
        }
    }
}