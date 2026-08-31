namespace Common.Security.Authenticators
{
    using Common.Security.Abstractions;
    using Common.Security.Constants;
    using Common.Security.Models;
    using Common.Security.Options;
    using Common.Security.Services.Functions;

    public sealed class MockAuthenticator : IAuthenticator
    {
        private static readonly log4net.ILog logger =
            log4net.LogManager.GetLogger(typeof(MockAuthenticator));

        private readonly string configuredPassword;
        private readonly bool allowAnyUser;

        private readonly HashSet<string> allowedUsers;
        private readonly HashSet<string> adminUsers;

        public MockAuthenticator(
            MockAuthenticationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            configuredPassword =
                options.Password ?? string.Empty;

            allowAnyUser =
                options.AllowAnyUser;

            allowedUsers =
                ParseUsers(options.AllowedUsers);

            adminUsers =
                ParseUsers(options.AdminUsers);
        }

        public AuthenticationResult Authenticate(
            string loginName,
            string password)
        {
            if (string.IsNullOrWhiteSpace(loginName))
            {
                logger.Warn(
                    "Mock authentication failed because the login name was blank.");

                return AuthenticationFailed();
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                logger.WarnFormat(
                    "Mock authentication failed because the supplied password was blank. LoginName={0}",
                    loginName);

                return AuthenticationFailed();
            }

            string userName =
                UserNameNormalizer.GetUserNameWithoutDomain(
                    loginName);

            if (string.IsNullOrWhiteSpace(userName))
            {
                logger.WarnFormat(
                    "Mock authentication failed because the normalized user name was blank. LoginName={0}",
                    loginName);

                return AuthenticationFailed();
            }

            // Fail closed.
            //
            // If no mock password is configured, mock
            // authentication must remain disabled.
            if (string.IsNullOrWhiteSpace(configuredPassword))
            {
                logger.Warn(
                    "Mock authentication is disabled because the mock password is not configured.");

                return AuthenticationFailed();
            }

            bool isAdministrative =
                adminUsers.Contains(userName);

            bool isAllowed =
                allowAnyUser ||
                allowedUsers.Contains(userName) ||
                isAdministrative;

            if (!isAllowed)
            {
                logger.WarnFormat(
                    "Mock authentication denied because the user is not allowed. UserName={0}",
                    userName);

                return AuthenticationFailed();
            }

            if (!string.Equals(
                    password,
                    configuredPassword,
                    StringComparison.Ordinal))
            {
                logger.WarnFormat(
                    "Mock authentication failed because the password did not match. UserName={0}",
                    userName);

                return AuthenticationFailed();
            }

            ApplicationUserInfo userInfo =
                CreateUserInfo(userName);

            logger.DebugFormat(
                "Mock authentication succeeded. UserName={0}, IsAdministrative={1}",
                userName,
                isAdministrative);

            return new AuthenticationResult
            {
                IsAuthenticated = true,
                IsAdministrative = isAdministrative,
                UserInfo = userInfo
            };
        }

        private static HashSet<string> ParseUsers(
            string? users)
        {
            if (string.IsNullOrWhiteSpace(users))
            {
                return new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
            }

            return users
                .Split(
                    [
                        StringConstants.Comma,
                        StringConstants.Semicolon
                    ],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(
                    UserNameNormalizer.GetUserNameWithoutDomain)
                .Where(
                    userName =>
                        !string.IsNullOrWhiteSpace(userName))
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
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

        private static ApplicationUserInfo CreateUserInfo(
            string userName)
        {
            return new ApplicationUserInfo
            {
                DisplayName = $"Mock {userName}",
                Mail = $"{userName}@mockdomain.org",
                Department = "Mock Department",
                Title = "Mock User",
                Manager = "Mock Manager",
                TelephoneNumber = "000-0000",
                Mobile = "000-0000",
                OtherTelephoneNumber = string.Empty,
                OtherMobile = string.Empty,
                Photo = null,
                UserAccountControl = 0,
                LastSync = DateTime.UtcNow
            };
        }
    }
}