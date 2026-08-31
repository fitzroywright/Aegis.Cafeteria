using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Secrets;
using Aegis.Cafeteria.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Workers;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Aegis.Cafeteria.Services");
builder.Configuration.AddEnvironmentVariables();

var secretProvider = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
var runtime = await CafeteriaRuntimeOptionsResolver.ResolveAsync(builder.Configuration, secretProvider);
if (!runtime.FfpIntegration.Enabled || string.IsNullOrWhiteSpace(runtime.FfpIntegration.ConnectionString))
    throw new InvalidOperationException("Aegis.Cafeteria.Services requires direct FFP integration and an FFP connection string/secret.");

var configuredManagement = builder.Configuration.GetSection("ManagementApi").Get<ManagementApiOptions>() ?? new();
var serviceApiKey = string.IsNullOrWhiteSpace(configuredManagement.ApiKey)
    ? await secretProvider.GetSecretAsync(ApplicationSecretNames.CafeteriaServicesApiKey)
    : configuredManagement.ApiKey.Trim();
var management = new ManagementApiOptions
{
    AuthorizedGroups = configuredManagement.AuthorizedGroups,
    ApiKey = string.IsNullOrWhiteSpace(serviceApiKey) ? null : serviceApiKey.Trim()
};
if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(management.ApiKey))
    throw new InvalidOperationException("Aegis.Cafeteria.Services requires CafeteriaServicesApiKey in non-Development environments.");
var monthOpening = builder.Configuration.GetSection("MonthOpening").Get<MonthOpeningOptions>() ?? new();
var scheduledEmail = builder.Configuration.GetSection("ScheduledEmail").Get<ScheduledEmailOptions>() ?? new();
var serviceDb = builder.Configuration["ServicePersistence:ConnectionString"] ?? "Data Source=cafeteria.services.db";
var sqliteBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(serviceDb);
if (!string.IsNullOrWhiteSpace(sqliteBuilder.DataSource) && sqliteBuilder.DataSource != ":memory:")
{
    var full = Path.GetFullPath(sqliteBuilder.DataSource);
    var directory = Path.GetDirectoryName(full);
    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
}

builder.Services.AddSingleton(runtime.FfpIntegration);
builder.Services.AddSingleton(management);
builder.Services.AddSingleton(monthOpening);
builder.Services.AddSingleton(scheduledEmail);
builder.Services.AddSingleton<MonthlyAllocationCalculator>();
builder.Services.AddSingleton<FfpSqlApplicationGateway>();
builder.Services.AddSingleton<IExternalAccountingGateway>(sp=>sp.GetRequiredService<FfpSqlApplicationGateway>());
builder.Services.AddSingleton<IWorkforceReferenceSource>(sp=>sp.GetRequiredService<FfpSqlApplicationGateway>());
builder.Services.AddSingleton<IProductCatalogSource>(sp=>sp.GetRequiredService<FfpSqlApplicationGateway>());
builder.Services.AddDbContextFactory<ServiceDbContext>(o=>o.UseSqlite(serviceDb));
builder.Services.AddSingleton<ServiceEventStore>();
builder.Services.AddSingleton<CustomerCreditPolicyStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAdditionalCreditAuthorizer, AuthenticatedAdditionalCreditAuthorizer>();
builder.Services.AddScoped<AdditionalCreditService>();

// Management financial operations are authenticated by Windows/AD. The client does not supply
// ApprovedBy/ProcessedBy; the service derives the actor from HttpContext.User.
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CafeteriaManagement", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            management.AuthorizedGroups.Length == 0 || management.AuthorizedGroups.Any(context.User.IsInRole));
    });
});

builder.Services.AddSingleton<MonthOpeningWorker>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<MonthOpeningWorker>());
builder.Services.AddSingleton<ServiceFfpPublisherWorker>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<ServiceFfpPublisherWorker>());
builder.Services.AddSingleton<FfpEmailReportSource>();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddHostedService<ScheduledEmailWorker>();

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<ServiceDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

// POS-to-Service integration uses a service credential. Management endpoints deliberately do not
// use this credential because financial actor identity must come from authenticated Windows/AD.
app.Use(async (context, next) =>
{
    var serviceEndpoint = context.Request.Path.StartsWithSegments("/api/pos") || context.Request.Path.StartsWithSegments("/api/ffp");
    if (serviceEndpoint && !string.IsNullOrWhiteSpace(management.ApiKey))
    {
        if (!context.Request.Headers.TryGetValue("X-Cafeteria-Api-Key", out var supplied) || supplied.Count != 1 || supplied[0] != management.ApiKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid Cafeteria Services API key.");
            return;
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status="running", application="Aegis.Cafeteria.Services", timeUtc=DateTimeOffset.UtcNow }));

app.MapGet("/api/pos/events", async (long afterSequence, int? take, ServiceEventStore store, CancellationToken ct)
    => Results.Ok(await store.GetAfterAsync(Math.Max(0,afterSequence), take ?? 100, ct)));

app.MapGet("/api/management/customers/{customerId:int}/credit-policy", async (int customerId, CustomerCreditPolicyStore policies, IWorkforceReferenceSource workforce, CancellationToken ct) =>
{
    var employee=(await workforce.GetCustomersAsync(ct)).SingleOrDefault(x=>x.CustomerId==customerId);
    if(employee is null) return Results.NotFound("Employee was not found in FFP.");
    var limit=await policies.GetAdditionalCreditLimitAsync(customerId,ct);
    return Results.Ok(new { customerId, employee.Name, AdditionalCreditLimit=limit });
}).RequireAuthorization("CafeteriaManagement");

app.MapPut("/api/management/customers/{customerId:int}/credit-policy", async (int customerId, CustomerCreditPolicyRequest request, HttpContext http, CustomerCreditPolicyStore policies, IWorkforceReferenceSource workforce, CancellationToken ct) =>
{
    if(request.AdditionalCreditLimit<0m) return Results.BadRequest("Additional Credit Limit cannot be negative.");
    var employee=(await workforce.GetCustomersAsync(ct)).SingleOrDefault(x=>x.CustomerId==customerId);
    if(employee is null) return Results.NotFound("Employee was not found in FFP.");
    var actor=RequireActor(http);
    var row=await policies.SetAdditionalCreditLimitAsync(customerId,request.AdditionalCreditLimit,actor,ct);
    return Results.Ok(new { row.CustomerId, employee.Name, row.AdditionalCreditLimit, row.UpdatedBy, row.UpdatedAt });
}).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/management/additional-credit", async (AdditionalCreditRequest request, HttpContext http, ServiceEventStore store, CustomerCreditPolicyStore policies, IWorkforceReferenceSource workforce, AdditionalCreditService additionalCredit, CancellationToken ct) =>
{
    if (request.CustomerId <= 0 || request.Amount <= 0m) return Results.BadRequest("Employee and a positive amount are required.");
    if (request.OperationId is Guid requestedId)
    {
        var existing=await store.GetByOperationAsync(requestedId,ct);
        if(existing is not null) return Results.Ok(existing);
    }

    var employees=await workforce.GetCustomersAsync(ct);
    var employee=employees.SingleOrDefault(x=>x.CustomerId==request.CustomerId);
    if(employee is null) return Results.NotFound("Employee was not found in FFP.");
    if(!employee.IsEligibleForCafeteriaCredit) return Results.BadRequest("Employee is not eligible for cafeteria credit.");

    var limit=await policies.GetAdditionalCreditLimitAsync(request.CustomerId,ct);
    if(limit is null) return Results.Problem("No employee-specific Additional Credit Limit has been configured for this employee.",statusCode:409);
    var at=request.ApprovedAt ?? DateTimeOffset.Now;
    var actor=RequireActor(http);
    var customer=new Customer(employee.CustomerId,employee.Name,employee.Photo,employee.IsEligibleForCafeteriaCredit,limit.Value);
    var period=new AccountPeriod(at.Year,at.Month,0);
    var existingApprovals=await store.GetAdditionalCreditApprovalsAsync(request.CustomerId,at.Year,at.Month,ct);
    try
    {
        var approval=additionalCredit.Approve(customer,period,existingApprovals,request.Amount,actor,at,request.Reason,request.OperationId);
        var payload=new AdditionalCreditServiceEvent(approval.Id,approval.CustomerId,approval.Amount,approval.ApprovedBy,approval.ApprovedAt,approval.Reason,limit.Value);
        var e=await store.AppendAsync(approval.Id,CafeteriaServiceEventKind.AdditionalCredit,approval.CustomerId,approval.ApprovedAt,payload,ct);
        return Results.Created($"/api/events/{e.Sequence}",e);
    }
    catch(UnauthorizedAccessException ex){return Results.Forbid();}
    catch(InvalidOperationException ex){return Results.BadRequest(ex.Message);}
    catch(ArgumentException ex){return Results.BadRequest(ex.Message);}
}).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/management/stipends", async (StipendRequest request, HttpContext http, ServiceEventStore store, IWorkforceReferenceSource workforce, CancellationToken ct) =>
{
    if(request.CustomerId<=0 || request.CashAmount<=0m || request.CardReductionAmount<0m || request.CardReductionAmount>request.CashAmount)
        return Results.BadRequest("Employee and valid cash/card-reduction amounts are required.");
    if(request.OperationId is Guid requestedId)
    {
        var existing=await store.GetByOperationAsync(requestedId,ct);
        if(existing is not null) return Results.Ok(existing);
    }
    var employees=await workforce.GetCustomersAsync(ct);
    var employee=employees.SingleOrDefault(x=>x.CustomerId==request.CustomerId);
    if(employee is null) return Results.NotFound("Employee was not found in FFP.");
    if(!employee.IsEligibleForCafeteriaCredit) return Results.BadRequest("Employee is not eligible for cafeteria credit.");
    var actor=RequireActor(http);
    var at=request.ProcessedAt ?? DateTimeOffset.Now;
    var operationId=request.OperationId.GetValueOrDefault(Guid.NewGuid());
    var payload=new StipendServiceEvent(operationId,request.CustomerId,request.CashAmount,request.CardReductionAmount,request.PaymentMode,actor,at,request.ExpenseReportReference,request.Notes);
    var e=await store.AppendAsync(operationId,CafeteriaServiceEventKind.Stipend,request.CustomerId,at,payload,ct);
    return Results.Created($"/api/events/{e.Sequence}",e);
}).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/month-opening/run", async (int? year, int? month, MonthOpeningWorker worker, CancellationToken ct) =>
{
    var now=DateTime.Now; var y=year ?? now.Year; var m=month ?? now.Month;
    if(m is <1 or >12) return Results.BadRequest("Month must be 1-12.");
    return Results.Ok(new { created=await worker.EnsureMonthAsync(y,m,ct), year=y, month=m });
}).RequireAuthorization("CafeteriaManagement");

app.MapGet("/api/ffp/customers", async (IWorkforceReferenceSource source,CustomerCreditPolicyStore policies,CancellationToken ct)=>
{
    var customers=await source.GetCustomersAsync(ct);
    var limits=await policies.GetAllAdditionalCreditLimitsAsync(ct);
    return Results.Ok(customers.Select(x=>x with { AdditionalCreditLimit=limits.GetValueOrDefault(x.CustomerId,0m) }).ToArray());
});
app.MapGet("/api/ffp/holidays", async (int year,int month,IWorkforceReferenceSource source,CancellationToken ct)=>Results.Ok(await source.GetHolidaysAsync(year,month,ct)));
app.MapGet("/api/ffp/catalog", async (IProductCatalogSource source,CancellationToken ct)=>Results.Ok(await source.GetCatalogAsync(ct)));
app.MapGet("/api/ffp/accounts", async (IExternalAccountingGateway gateway,CancellationToken ct)=>Results.Ok(await gateway.GetAccountSummariesAsync(ct)));
app.MapPost("/api/ffp/ledger", async (ExternalLedgerEntry entry,IExternalAccountingGateway gateway,CancellationToken ct)=>{await gateway.PublishLedgerEntryAsync(entry,ct);return Results.Ok();});

await app.RunAsync();

static string RequireActor(HttpContext http)
{
    var name=http.User.Identity?.Name;
    if(http.User.Identity?.IsAuthenticated!=true || string.IsNullOrWhiteSpace(name))
        throw new UnauthorizedAccessException("Authenticated management identity is required.");
    return name.Trim();
}
