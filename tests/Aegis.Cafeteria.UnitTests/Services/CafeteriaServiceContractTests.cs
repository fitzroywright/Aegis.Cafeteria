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
    public void StipendEvent_RoundTripsWithoutLosingSeparateCashAndCardReduction()
    {
        var id=Guid.NewGuid();
        var x=new StipendServiceEvent(id,101,800m,650m,StipendPaymentMode.Advance,"Manager",DateTimeOffset.UtcNow,"EXP-1","Off-site meal");
        var json=JsonSerializer.Serialize(x);
        var y=JsonSerializer.Deserialize<StipendServiceEvent>(json)!;
        Assert.Equal(800m,y.CashAmount);
        Assert.Equal(650m,y.CardReductionAmount);
        Assert.Equal(id,y.OperationId);
    }
}
