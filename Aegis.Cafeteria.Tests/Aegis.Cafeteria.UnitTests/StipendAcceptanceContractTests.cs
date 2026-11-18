using System.Reflection;
using System.Text.Json;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Workers;

namespace Aegis.Cafeteria.UnitTests;

public sealed class StipendAcceptanceContractTests
{
    [Fact]
    public void Actual_outbound_worker_preserves_deduction_contract_and_bridge_scan_type()
    {
        Guid id = Guid.NewGuid(); DateTimeOffset at = DateTimeOffset.UtcNow;
        var payload = new StipendServiceEvent(id, 991, 125m, "Acceptance.Engineer", at, "Off-site meal");
        var envelope = new CafeteriaServiceEventEnvelope(1, id, CafeteriaServiceEventKind.Stipend, 991, at, JsonSerializer.Serialize(payload));
        var method = typeof(ExternalPublicationWorker).GetMethod("BuildTransaction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var outbound = (CafeteriaOutboundTransaction)method.Invoke(null, [envelope])!;
        Assert.Equal(id, outbound.TransactionId); Assert.Equal("StipendCardReduction", outbound.TransactionKind);
        Assert.Equal(125m, outbound.Amount); Assert.Equal(payload.ProcessedBy, outbound.UserName);
        Assert.Contains(payload.Reason!, outbound.Description);
        string root = FindRoot();
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string plugin = Path.Combine(root, "Aegis.Cafeteria.Services", "bin", configuration, "net11.0", "plugins", "Aegis.Cafeteria.Plugin.Integration.dll");
        var provider = Assembly.LoadFrom(plugin).GetType("Aegis.Cafeteria.Plugin.Integration.FfpManagerIntegrationProvider")!;
        var scanType = provider.GetMethod("ResolveScanType", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(4, (int)scanType.Invoke(null, [outbound.TransactionKind])!);
        using var json = JsonDocument.Parse(envelope.PayloadJson);
        Assert.True(json.RootElement.TryGetProperty("Amount", out _));
        Assert.False(json.RootElement.TryGetProperty("StipendPaymentMode", out _));
        Assert.False(json.RootElement.TryGetProperty("CashAmount", out _));
        Assert.False(json.RootElement.TryGetProperty("CardReductionAmount", out _));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Invalid_stipend_cannot_be_acknowledged_as_successfully_published(decimal amount)
    {
        var payload = new StipendServiceEvent(Guid.NewGuid(), 991, amount, "Engineer", DateTimeOffset.UtcNow, "Invalid input");
        var envelope = new CafeteriaServiceEventEnvelope(1, payload.OperationId, CafeteriaServiceEventKind.Stipend, 991, payload.ProcessedAt, JsonSerializer.Serialize(payload));
        var method = typeof(ExternalPublicationWorker).GetMethod("BuildTransaction", BindingFlags.Static | BindingFlags.NonPublic)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [envelope]));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }
    private static string FindRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "Aegis.Cafeteria.Services"))) return current.FullName;
        throw new DirectoryNotFoundException("Cafeteria repository root was not found.");
    }
}
