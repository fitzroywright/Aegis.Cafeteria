namespace Aegis.Cafeteria.Infrastructure.Security;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Security;

public sealed class CommonSecurityRefundAuthorizer(IManagerAuthorizationRegistry registry) : IRefundAuthorizer
{
    public bool CanRefund(string userId) => registry.IsAuthorized(userId);
}

public sealed class CommonSecurityOrderVoidAuthorizer(IManagerAuthorizationRegistry registry) : IOrderVoidAuthorizer
{
    public bool CanVoid(string userId) => registry.IsAuthorized(userId);
}
