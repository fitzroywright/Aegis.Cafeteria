namespace Aegis.Cafeteria.Infrastructure.Persistence;

using System.Collections.Concurrent;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;
using Microsoft.EntityFrameworkCore;

public sealed class EfCustomerIdentificationService(IDbContextFactory<LocalCafeteriaDbContext> factory, ICustomerAccountQuery accountQuery) : ICustomerIdentificationService
{
    private readonly ConcurrentDictionary<int, CustomerIdentificationContext> identificationContexts = new();

    public async Task<CustomerAccountView?> IdentifyAsync(CustomerIdentifierType type,string value,CancellationToken cancellationToken=default)
    {
        if(string.IsNullOrWhiteSpace(value)) return null; string normalized=DomainPersistenceMapper.NormalizeIdentifier(type,value); if(string.IsNullOrWhiteSpace(normalized)) return null; await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken); int? customerId=await db.CustomerIdentifiers.AsNoTracking().Where(x=>x.Type==(int)type&&x.NormalizedValue==normalized).Select(x=>(int?)x.CustomerId).SingleOrDefaultAsync(cancellationToken); if(!customerId.HasValue)return null; IDMethod method=type switch{CustomerIdentifierType.NfcTag=>IDMethod.NFC,CustomerIdentifierType.Code=>IDMethod.Code,CustomerIdentifierType.Trn=>IDMethod.TRN,_=>throw new ArgumentOutOfRangeException(nameof(type),type,"Unsupported customer identifier type.")}; CustomerIdentificationContext context=new(method,value.Trim()); identificationContexts.Clear(); CustomerAccountView? account=await accountQuery.GetCurrentAsync(customerId.Value,cancellationToken); return account is null?null:account with{Identification=context};
    }

    public async Task<IReadOnlyList<CustomerSearchCandidate>> SearchAsync(string searchText,int maximumResults=20,CancellationToken cancellationToken=default)
    {
        maximumResults=Math.Clamp(maximumResults,1,50); string rawSearch=searchText?.Trim()??string.Empty; string normalized=DomainPersistenceMapper.NormalizeSearchText(rawSearch); identificationContexts.Clear(); await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(cancellationToken);
        List<int> matchedIds;
        if(rawSearch.Length==0)
        {
            // Empty search is an explicit directory browse. Show every cached employee and expose
            // eligibility in the candidate so the cashier can see why a person cannot charge an account.
            matchedIds=await db.Customers.AsNoTracking().OrderBy(x=>x.NormalizedName).Select(x=>x.Id).Take(maximumResults).ToListAsync(cancellationToken);
        }
        else
        {
            matchedIds=await db.Customers.AsNoTracking().Where(x=>x.IsEligibleForCredit&&x.NormalizedName.Contains(normalized)).OrderBy(x=>x.NormalizedName).Select(x=>x.Id).Take(maximumResults).ToListAsync(cancellationToken);
        }
        if(matchedIds.Count==0)return[]; List<Models.CustomerRow> customers=await db.Customers.AsNoTracking().Where(x=>matchedIds.Contains(x.Id)).ToListAsync(cancellationToken); foreach(int customerId in matchedIds) identificationContexts[customerId]=new CustomerIdentificationContext(IDMethod.Name,"NAME SEARCH"); return matchedIds.Select(id=>customers.Single(x=>x.Id==id)).Select(x=>new CustomerSearchCandidate(x.Id,x.Name,x.IsEligibleForCredit,x.Photo,x.Department,x.Manager,x.EmailAddress,x.Extension)).ToArray();
    }

    public async Task<CustomerAccountView?> LoadAsync(int customerId,CancellationToken cancellationToken=default)
    {
        CustomerAccountView? account=await accountQuery.GetCurrentAsync(customerId,cancellationToken); if(account is null)return null; bool identifiedByName=identificationContexts.TryGetValue(customerId,out CustomerIdentificationContext? context); identificationContexts.Clear(); return identifiedByName?account with{Identification=context}:account;
    }
}
