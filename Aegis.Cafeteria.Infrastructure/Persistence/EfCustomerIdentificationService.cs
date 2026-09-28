namespace Aegis.Cafeteria.Infrastructure.Persistence;

using System.Collections.Concurrent;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public sealed class EfCustomerIdentificationService(IDbContextFactory<LocalCafeteriaDbContext> factory, ICustomerAccountQuery accountQuery, ILogger<EfCustomerIdentificationService>? logger = null) : ICustomerIdentificationService
{
    private readonly ConcurrentDictionary<int, CustomerIdentificationContext> identificationContexts = new();

    public async Task<CustomerAccountView?> IdentifyAsync(CustomerIdentifierType type,string value,CancellationToken cancellationToken=default)
    {
        Stopwatch total = Stopwatch.StartNew();
        if(string.IsNullOrWhiteSpace(value)) return null;
        string normalized=DomainPersistenceMapper.NormalizeIdentifier(type,value);
        if(string.IsNullOrWhiteSpace(normalized)) return null;

        Stopwatch step = Stopwatch.StartNew();
        await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken);
        logger?.LogInformation("PERF Identify {IdentifierType} DbContext created in {ElapsedMs} ms", type, step.ElapsedMilliseconds);

        step.Restart();
        int? customerId=await db.CustomerIdentifiers.AsNoTracking()
            .Where(x=>x.Type==(int)type&&x.NormalizedValue==normalized
                && db.Customers.Any(customer=>customer.Id==x.CustomerId&&customer.IsActive&&customer.IsEligibleForCredit))
            .Select(x=>(int?)x.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);
        logger?.LogInformation("PERF Identify {IdentifierType} identifier lookup in {ElapsedMs} ms; Found={Found}", type, step.ElapsedMilliseconds, customerId.HasValue);

        if(!customerId.HasValue)
        {
            logger?.LogInformation("PERF Identify {IdentifierType} total {ElapsedMs} ms; no match", type, total.ElapsedMilliseconds);
            return null;
        }

        IDMethod method=type switch{CustomerIdentifierType.NfcTag=>IDMethod.NFC,CustomerIdentifierType.Code=>IDMethod.Code,CustomerIdentifierType.Trn=>IDMethod.TRN,_=>throw new ArgumentOutOfRangeException(nameof(type),type,"Unsupported customer identifier type.")};
        CustomerIdentificationContext context=new(method,value.Trim());
        identificationContexts.Clear();

        step.Restart();
        CustomerAccountView? account=await accountQuery.GetCurrentSummaryAsync(customerId.Value,cancellationToken);
        logger?.LogInformation("PERF Identify {IdentifierType} account summary for CustomerId={CustomerId} in {ElapsedMs} ms", type, customerId.Value, step.ElapsedMilliseconds);
        logger?.LogInformation("PERF Identify {IdentifierType} total {ElapsedMs} ms", type, total.ElapsedMilliseconds);
        return account is null?null:account with{Identification=context};
    }

    public async Task<IReadOnlyList<CustomerSearchCandidate>> SearchAsync(string searchText,int maximumResults=20,CancellationToken cancellationToken=default)
    {
        maximumResults=Math.Clamp(maximumResults,1,50); string rawSearch=searchText?.Trim()??string.Empty; string normalized=DomainPersistenceMapper.NormalizeSearchText(rawSearch); identificationContexts.Clear(); await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken);
        List<int> matchedIds;
        if(rawSearch.Length==0)
        {
            // Directory browsing uses the same active and eligible roster as name and card lookup.
            matchedIds=await db.Customers.AsNoTracking().Where(x=>x.IsActive&&x.IsEligibleForCredit).OrderBy(x=>x.NormalizedName).Select(x=>x.Id).Take(maximumResults).ToListAsync(cancellationToken);
        }
        else
        {
            matchedIds=await db.Customers.AsNoTracking().Where(x=>x.IsActive&&x.IsEligibleForCredit&&x.NormalizedName.Contains(normalized)).OrderBy(x=>x.NormalizedName).Select(x=>x.Id).Take(maximumResults).ToListAsync(cancellationToken);
        }
        if(matchedIds.Count==0)return[];
        List<Models.CustomerRow> customers=await db.Customers.AsNoTracking().Where(x=>matchedIds.Contains(x.Id)).ToListAsync(cancellationToken);
        const string nameSearchCode = "NAME SEARCH";
        foreach(int customerId in matchedIds)
            identificationContexts[customerId]=new CustomerIdentificationContext(IDMethod.Name,nameSearchCode);
        return matchedIds.Select(id=>customers.Single(x=>x.Id==id)).Select(x=>new CustomerSearchCandidate(x.Id,x.Name,x.IsEligibleForCredit,x.Photo,x.Department,x.Manager,x.EmailAddress,x.Extension)).ToArray();
    }

    public async Task<CustomerAccountView?> LoadAsync(int customerId,CancellationToken cancellationToken=default)
    {
        Stopwatch total = Stopwatch.StartNew();
        await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.Customers.AsNoTracking().AnyAsync(customer=>customer.Id==customerId&&customer.IsActive&&customer.IsEligibleForCredit,cancellationToken))
        {
            identificationContexts.TryRemove(customerId, out _);
            return null;
        }
        CustomerAccountView? account=await accountQuery.GetCurrentSummaryAsync(customerId,cancellationToken);
        logger?.LogInformation("PERF Customer Load summary CustomerId={CustomerId} in {ElapsedMs} ms", customerId, total.ElapsedMilliseconds);
        if(account is null)return null;
        bool identifiedByName=identificationContexts.TryGetValue(customerId,out CustomerIdentificationContext? context);
        identificationContexts.Clear();
        return identifiedByName?account with{Identification=context}:account;
    }
}

