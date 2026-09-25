namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;

public sealed class FfpGatewayCapabilityBoundaryTests
{
    [Fact]
    public void FocusedGateways_ExposeOnlyTheirIntendedApplicationCapability()
    {
        Assert.Equal(
            [typeof(IExternalAccountingGateway)],
            ApplicationCapabilities(typeof(FfpAccountingGateway)));

        Assert.Equal(
            [typeof(IFundingPolicyCompatibilityGateway)],
            ApplicationCapabilities(typeof(FfpFundingPolicyCompatibilityGateway)));

        Assert.Equal(
            [typeof(IMonthRecalculationGuard)],
            ApplicationCapabilities(typeof(FfpMonthRecalculationGuard)));

        Assert.Equal(
            [typeof(IDevelopmentDataResetGateway)],
            ApplicationCapabilities(typeof(FfpDevelopmentDataResetGateway)));

        Assert.Equal(
            [typeof(IWorkforceReferenceSource)],
            ApplicationCapabilities(typeof(FfpEmployeeWorkforceSource)));
    }

    [Fact]
    public async Task WriteConnection_IsRejectedBeforeConnecting_WhenWritesAreDisabled()
    {
        FfpIntegrationOptions options = new()
        {
              Enabled = true
            , AllowWrites = false
            , ConnectionString = "Server=not-used;Database=not-used;Integrated Security=true;TrustServerCertificate=true"
        };

        FfpSqlConnectionFactory factory = new(options);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.OpenWriteAsync(CancellationToken.None));

        Assert.Contains("writes are disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Type[] ApplicationCapabilities(Type gatewayType)
        => gatewayType.GetInterfaces()
            .Where(type => type.Namespace == typeof(IExternalAccountingGateway).Namespace)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
}
