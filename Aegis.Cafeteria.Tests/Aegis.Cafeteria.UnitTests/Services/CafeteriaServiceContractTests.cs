namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Domain.Accounts;
using System.Text.Json;

public sealed class CafeteriaServiceContractTests
{

    [Fact]
    public void AdditionalCreditEvent_CarriesEmployeeSpecificLimitForImmediatePosRecalculation()
    {
        var id=Guid.NewGuid();
        var x=new AdditionalCreditServiceEvent(id,101,1_500m,"FFTPJ\\Manager",DateTimeOffset.UtcNow,"Approved",5_000m);
        var json=JsonSerializer.Serialize(x);
        var y=JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(json)!;
        Assert.Equal(5_000m,y.AdditionalCreditLimit);
        Assert.Equal("FFTPJ\\Manager",y.ApprovedBy);
    }

    [Fact]
    public void StipendEvent_RoundTripsDeductionAmountAndReason()
    {
        var id=Guid.NewGuid();
        var x=new StipendServiceEvent(id,101,650m,"Manager",DateTimeOffset.UtcNow,"Off-site meal");
        var json=JsonSerializer.Serialize(x);
        var y=JsonSerializer.Deserialize<StipendServiceEvent>(json)!;
        Assert.Equal(650m,y.Amount);
        Assert.Equal("Off-site meal",y.Reason);
        Assert.Equal(id,y.OperationId);
    }
    [Fact]
    public void ProductCatalogImportEvent_RoundTripsManagementImportMetadata()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset at = DateTimeOffset.UtcNow;
        ProductCatalogImportServiceEvent source = new(id, 42, "LUNCH42", "Chicken Lunch", 700m, "Manager", at);

        string json = JsonSerializer.Serialize(source);
        ProductCatalogImportServiceEvent? result = JsonSerializer.Deserialize<ProductCatalogImportServiceEvent>(json);

        Assert.NotNull(result);
        Assert.Equal(id, result!.OperationId);
        Assert.Equal("Chicken Lunch", result.Name);
        Assert.Equal(700m, result.Price);
        Assert.Equal("Manager", result.ImportedBy);
    }

    [Fact]
    public void LegacyTransactionImportEvent_RoundTripsSourceIdentity()
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset at = DateTimeOffset.UtcNow;
        LegacyTransactionImportServiceEvent source = new
        (
              id
            , 12345
            , 101
            , 2026
            , 9
            , at
            , "Lunch"
            , "Imported purchase"
            , 650m
            , "SYSTEM"
            , "CARD"
            , 8
            , "Manager"
            , at
        );

        string json = JsonSerializer.Serialize(source);
        LegacyTransactionImportServiceEvent? result = JsonSerializer.Deserialize<LegacyTransactionImportServiceEvent>(json);

        Assert.NotNull(result);
        Assert.Equal(12345, result!.SourceId);
        Assert.Equal(101, result.CustomerId);
        Assert.Equal(8, result.ScanType);
        Assert.Equal("Manager", result.ImportedBy);
    }

}
