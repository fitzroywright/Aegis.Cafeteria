using Aegis.Cafeteria.Services;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

public sealed class ManagementRolePolicyTests
{
    [Fact]
    public void AdministrativeAccessRequiresApprovedManagerAndAnExplicitImmutableAccountGrant()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["ManagementApi:AccountRoles:approved-id"]=CafeteriaRoles.Administrator,
            ["ManagementApi:AccountRoles:username-alias"]=CafeteriaRoles.Administrator }).Build();
        Assert.Null(CafeteriaRoleResolver.ResolveManagementRole(config,"approved-id",false));
        Assert.Equal(CafeteriaRoles.Administrator,CafeteriaRoleResolver.ResolveManagementRole(config,"approved-id",true));
        Assert.Equal(CafeteriaRoles.Manager,CafeteriaRoleResolver.ResolveManagementRole(config,"different-immutable-id",true));
        config["ManagementApi:AccountRoles:approved-id"]=null;
        var role=CafeteriaRoleResolver.ResolveManagementRole(config,"approved-id",true);
        Assert.Equal(CafeteriaRoles.Manager,role);
        Assert.False(CafeteriaPermissions.HasPermission(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,role!)},"verified-session")),CafeteriaPermissions.ManageUsers));
    }
}
