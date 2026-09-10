namespace Aegis.Cafeteria.UnitTests.Application;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class MonthlyCreditAllocationServiceTests
{
    [Fact]
    public async Task EnsureAsync_EligibleEmployee_UsesCreditAndSubsidyPolicySeparately()
    {
        var store=new Store();
        var service=new MonthlyCreditAllocationService(new Days(21),new Policy(new CafeteriaRatePolicy("FFP00", 800m, "FFP00", 650m, DateTimeOffset.UtcNow)),store);
        var allocation=await service.EnsureAsync(new Customer(1,"Customer",null,true,2000m),2026,8);
        Assert.Equal(16_800m, allocation.TotalCreditLimit);
        Assert.Equal(800m, allocation.CreditLimitPrice);
        Assert.Equal(650m, allocation.SubsidyCost);
        Assert.Equal(13_650m, allocation.SubsidyAllocation);
        Assert.Equal(3_150m, allocation.EmployeeFundedAllocation);
        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task EnsureAsync_ExistingAllocation_IsImmutableAndReused()
    {
        var existing=new MonthlyCreditAllocation(1,2026,8,20, true, 600m, 600m, DateTimeOffset.UtcNow);
        var store=new Store(existing); var policy=new TrackingPolicy(new CafeteriaRatePolicy("FFP00", 999m, "FFP00", 999m, DateTimeOffset.UtcNow));
        var service=new MonthlyCreditAllocationService(new Days(99),policy,store);
        var result=await service.EnsureAsync(new Customer(1,"Customer",null,true,0m),2026,8);
        Assert.Same(existing,result); Assert.Equal(0,policy.Calls); Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task EnsureAsync_MissingFfpRatePolicy_ReturnsTemporaryZeroAndDoesNotPersist()
    {
        var store=new Store(); var service=new MonthlyCreditAllocationService(new Days(20),new Policy(null),store);
        var result=await service.EnsureAsync(new Customer(1,"Customer",null,true,0m),2026,8);
        Assert.Equal(0m,result.TotalCreditLimit); Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task EnsureAsync_IneligibleEmployee_BypassesWorkingDaysAndFfpRatePolicy()
    {
        var days=new TrackingDays(); var policy=new TrackingPolicy(new CafeteriaRatePolicy("FFP00", 650m, "FFP00", 650m, DateTimeOffset.UtcNow)); var store=new Store();
        var result=await new MonthlyCreditAllocationService(days,policy,store).EnsureAsync(new Customer(1,"Cash Only",null,false,0m),2026,8);
        Assert.Equal(0m,result.TotalCreditLimit); Assert.Equal(0,days.Calls); Assert.Equal(0,policy.Calls); Assert.Empty(store.Saved);
    }

    private sealed class Days(int value):IWorkingDayProvider{public Task<int> GetWorkingDaysAsync(int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult(value);}
    private sealed class TrackingDays:IWorkingDayProvider{public int Calls{get;private set;} public Task<int> GetWorkingDaysAsync(int year,int month,CancellationToken cancellationToken=default){Calls++;return Task.FromResult(20);}}
    private sealed class Policy(CafeteriaRatePolicy? value):ICafeteriaRatePolicyProvider{public Task<CafeteriaRatePolicy?> GetAsync(CancellationToken cancellationToken=default)=>Task.FromResult(value);}
    private sealed class TrackingPolicy(CafeteriaRatePolicy? value):ICafeteriaRatePolicyProvider{public int Calls{get;private set;} public Task<CafeteriaRatePolicy?> GetAsync(CancellationToken cancellationToken=default){Calls++;return Task.FromResult(value);}}
    private sealed class Store(MonthlyCreditAllocation? existing=null):IMonthlyCreditAllocationStore
    {
        public List<MonthlyCreditAllocation> Saved{get;}=[];
        public Task<MonthlyCreditAllocation?> GetAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult(existing);
        public Task SaveAsync(MonthlyCreditAllocation allocation,CancellationToken cancellationToken=default){Saved.Add(allocation);return Task.CompletedTask;}
    }
}
