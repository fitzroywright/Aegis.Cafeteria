namespace Aegis.Cafeteria.UnitTests.Reporting;

using Aegis.Cafeteria.Application.Messaging;
using Aegis.Cafeteria.Application.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class WeeklyCustomerSummaryServiceTests
{
    [Fact]
    public async Task SendsOnce_AndUsesEmployeeIdForAegisMessaging()
    {
        var start=new DateOnly(2026,8,10); var end=start.AddDays(6);
        var q=new Query([new WeeklyTransactionSummary(77,"Test Employee",start,end,3,1500m,1000m,500m,0m,3000m)]);
        var store=new Dispatch(); var gateway=new Messages();
        var sut=new WeeklyCustomerSummaryService(q,store,gateway,NullLogger<WeeklyCustomerSummaryService>.Instance);
        Assert.Equal(1,await sut.SendWeekAsync(start,end)); Assert.Equal(0,await sut.SendWeekAsync(start,end));
        Assert.Equal(77,Assert.Single(gateway.Items).EmployeeId);
    }
    private sealed class Query(IReadOnlyList<WeeklyTransactionSummary> x):IWeeklyTransactionSummaryQuery{public Task<IReadOnlyList<WeeklyTransactionSummary>> GetAsync(DateOnly a,DateOnly b,CancellationToken c=default)=>Task.FromResult(x);}
    private sealed class Dispatch:IWeeklySummaryDispatchStore{private readonly HashSet<(int,DateOnly)> s=[];public Task<bool> WasSentAsync(int i,DateOnly w,CancellationToken c=default)=>Task.FromResult(s.Contains((i,w)));public Task MarkSentAsync(int i,DateOnly w,Guid m,DateTimeOffset t,CancellationToken c=default){s.Add((i,w));return Task.CompletedTask;}}
    private sealed class Messages:ICafeteriaMessagingGateway{public List<CafeteriaEmployeeMessage> Items{get;}=[];public Task SendAsync(CafeteriaEmployeeMessage m,CancellationToken c=default){Items.Add(m);return Task.CompletedTask;}}
}
