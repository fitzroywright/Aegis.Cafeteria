using Aegis.Cafeteria.Infrastructure.Registration;

namespace Aegis.Cafeteria.UnitTests;

public sealed class CafeteriaControlPlaneRegistrationTests
{
    [Theory]
    [InlineData("CAFETERIA-POS", "Windows", "CAFETERIA-POS-Windows")]
    [InlineData("CAFETERIA-POS", "Linux", "CAFETERIA-POS-Linux")]
    public void Pos_instance_identity_is_terminal_and_platform_scoped(
        string terminalId,
        string platform,
        string expected)
    {
        Assert.Equal(
            expected,
            CafeteriaRegistrationIdentity.BuildInstanceId(terminalId, platform));
    }

    [Theory]
    [InlineData("Aegis.Cafeteria.Services")]
    [InlineData("Aegis.Cafeteria.POS")]
    [InlineData("Aegis.Cafeteria.POS.UI")]
    [InlineData("Aegis.Cafeteria.Management")]
    public void Cafeteria_module_ids_are_fully_qualified(string module)
    {
        Assert.True(module.StartsWith("Aegis.Cafeteria.", StringComparison.Ordinal));
    }

    [Fact]
    public void Pos_and_ui_have_separate_durable_identity_files()
    {
        const string instanceId = "CAFETERIA-POS-Windows";

        string pos = CafeteriaRegistrationIdentity.BuildIdentityFilePath(
            "Aegis.Cafeteria.POS",
            instanceId);
        string ui = CafeteriaRegistrationIdentity.BuildIdentityFilePath(
            "Aegis.Cafeteria.POS.UI",
            instanceId);

        Assert.NotEqual(pos, ui);
        Assert.True(pos.EndsWith("registration-identity.json", StringComparison.OrdinalIgnoreCase));
        Assert.True(ui.EndsWith("registration-identity.json", StringComparison.OrdinalIgnoreCase));
    }
}
