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
using Aegis.Cafeteria.Services.Development;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Realtime;
using Aegis.Cafeteria.Services.Workers;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Aegis.Cafeteria.Services");
builder.Configuration.AddEnvironmentVariables();

var secretProvider = ApplicationSecretProviderFactory.Create(builder.Configuration, builder.Environment);
var runtime = await CafeteriaRuntimeOptionsResolver.ResolveAsync(builder.Configuration, secretProvider);
bool developmentDataEnabled = builder.Configuration.GetSection("DevelopmentData").GetValue("Enabled", false);
if (developmentDataEnabled && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("DevelopmentData can only be enabled in the Development environment.");
if (!developmentDataEnabled && (!runtime.FfpIntegration.Enabled || string.IsNullOrWhiteSpace(runtime.FfpIntegration.ConnectionString)))
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
if (developmentDataEnabled)
{
    builder.Services.AddSingleton<DevelopmentFfpGateway>();
    builder.Services.AddSingleton<IExternalAccountingGateway>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IWorkforceReferenceSource>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IFundingPolicyCompatibilityGateway>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IMonthRecalculationGuard>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IDevelopmentDataResetGateway>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<DevelopmentPayrollLedgerSource>();
    builder.Services.AddSingleton<IPayrollLedgerSource>(sp => sp.GetRequiredService<DevelopmentPayrollLedgerSource>());
}
else
{
    builder.Services.AddSingleton<FfpSqlApplicationGateway>();
    builder.Services.AddSingleton<IExternalAccountingGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<IWorkforceReferenceSource>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<IFundingPolicyCompatibilityGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<IMonthRecalculationGuard>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<IDevelopmentDataResetGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<FfpPayrollLedgerSource>();
    builder.Services.AddSingleton<IPayrollLedgerSource>(sp => sp.GetRequiredService<FfpPayrollLedgerSource>());
}
builder.Services.AddDbContextFactory<ServiceDbContext>(o=>o.UseSqlite(serviceDb));
builder.Services.AddSignalR();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}
builder.Services.AddSingleton<ServiceEventRealtimePublisher>();
builder.Services.AddSingleton<ServiceEventStore>();
builder.Services.AddSingleton<CustomerCreditPolicyStore>();
builder.Services.AddSingleton<CafeteriaFundingPolicyStore>();
builder.Services.AddSingleton<FundingPolicyManagementService>();
builder.Services.AddSingleton<ICafeteriaFundingPolicySource>(sp => sp.GetRequiredService<CafeteriaFundingPolicyStore>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAdditionalCreditAuthorizer, AuthenticatedAdditionalCreditAuthorizer>();
builder.Services.AddScoped<AdditionalCreditService>();
builder.Services.AddScoped<IStipendAuthorizer, AuthenticatedStipendAuthorizer>();
builder.Services.AddScoped<StipendService>();
builder.Services.AddSingleton<StipendAccountSnapshotFactory>();

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

builder.Services.AddSingleton<DevelopmentDataResetService>();
builder.Services.AddSingleton<MonthOpeningWorker>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<MonthOpeningWorker>());
builder.Services.AddSingleton<ServiceFfpPublisherWorker>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<ServiceFfpPublisherWorker>());
builder.Services.AddSingleton<FfpEmailReportSource>();
builder.Services.AddSingleton<PayrollDeductionReportSource>();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<ScheduledEmailWorker>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<ScheduledEmailWorker>());

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<ServiceDbContext>>();
    await using var db=await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();

    await db.Database.ExecuteSqlRawAsync
    (
        @"CREATE TABLE IF NOT EXISTS CafeteriaFundingPolicy (
              Id INTEGER NOT NULL CONSTRAINT PK_CafeteriaFundingPolicy PRIMARY KEY
            , DailyCreditLimitRate TEXT NOT NULL
            , DailyCompanySubsidyRate TEXT NOT NULL
            , UpdatedBy TEXT NOT NULL
            , UpdatedAt TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS CafeteriaFundingPolicyHistory (
              Id INTEGER NOT NULL CONSTRAINT PK_CafeteriaFundingPolicyHistory PRIMARY KEY AUTOINCREMENT
            , DailyCreditLimitRate TEXT NOT NULL
            , DailyCompanySubsidyRate TEXT NOT NULL
            , UpdatedBy TEXT NOT NULL
            , UpdatedAt TEXT NOT NULL
        );"
    );

    CafeteriaFundingPolicyStore fundingPolicyStore = scope.ServiceProvider.GetRequiredService<CafeteriaFundingPolicyStore>();
    bool hasFundingPolicy = await db.FundingPolicies.AnyAsync();

    if (!hasFundingPolicy)
    {
        decimal bootstrapCreditRate = builder.Configuration.GetSection("FundingPolicy").GetValue("DailyCreditLimitRate", 0m);
        decimal bootstrapSubsidyRate = builder.Configuration.GetSection("FundingPolicy").GetValue("DailyCompanySubsidyRate", 0m);

        if (bootstrapCreditRate > 0m && bootstrapSubsidyRate > 0m)
        {
            await fundingPolicyStore.SetFundingPolicyAsync
            (
                  bootstrapCreditRate
                , bootstrapSubsidyRate
                , "CONFIGURATION"
            );
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// POS-to-Service integration uses a service credential. Management endpoints deliberately do not
// use this credential because financial actor identity must come from authenticated Windows/AD.
app.Use(async (context, next) =>
{
    bool serviceEndpoint = context.Request.Path.StartsWithSegments("/api/pos")
        || context.Request.Path.StartsWithSegments("/api/ffp")
        || context.Request.Path.StartsWithSegments("/hubs/cafeteria");
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

app.MapHub<ManagementEventsHub>("/hubs/cafeteria");

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

app.MapPost("/api/management/stipends", async
(
      StipendRequest request
    , HttpContext http
    , ServiceEventStore store
    , IWorkforceReferenceSource workforce
    , StipendAccountSnapshotFactory accountFactory
    , StipendService stipendService
    , CancellationToken ct
) =>
{
    if (request.CustomerId <= 0 || request.CashAmount <= 0m || request.CardReductionAmount < 0m)
    {
        return Results.BadRequest("Employee and valid cash/card-reduction amounts are required.");
    }

    if (request.OperationId is Guid requestedId)
    {
        CafeteriaServiceEventEnvelope? existing = await store.GetByOperationAsync(requestedId, ct);

        if (existing is not null)
        {
            return Results.Ok(existing);
        }
    }

    IReadOnlyList<ExternalCustomerSnapshot> employees = await workforce.GetCustomersAsync(ct);
    ExternalCustomerSnapshot? employee = employees.SingleOrDefault(candidate => candidate.CustomerId == request.CustomerId);

    if (employee is null)
    {
        return Results.NotFound("Employee was not found in FFP.");
    }

    if (!employee.IsEligibleForCafeteriaCredit)
    {
        return Results.BadRequest("Employee is not eligible for cafeteria credit.");
    }

    string actor = RequireActor(http);
    DateTimeOffset processedAt = request.ProcessedAt ?? DateTimeOffset.Now;

    try
    {
        CustomerAccountSnapshot account = await accountFactory.BuildAsync
        (
              request.CustomerId
            , processedAt.Year
            , processedAt.Month
            , ct
        );

        Customer customer = new(employee.CustomerId, employee.Name, employee.Photo, true, 0m);
        Stipend stipend = stipendService.Process
        (
              customer
            , account
            , request.CashAmount
            , request.PaymentMode
            , actor
            , processedAt
            , request.ExpenseReportReference
            , request.Notes
        );

        if (request.CardReductionAmount != stipend.CardReduction)
        {
            return Results.BadRequest
            (
                $"Card reduction must be {stipend.CardReduction:N2} for the employee's current subsidy and cafeteria balance."
            );
        }

        Guid operationId = request.OperationId.GetValueOrDefault(Guid.NewGuid());
        StipendServiceEvent payload = new
        (
              operationId
            , request.CustomerId
            , stipend.CashAmount
            , stipend.CardReduction
            , stipend.PaymentMode
            , actor
            , stipend.ProcessedAt
            , stipend.ExpenseReportReference
            , stipend.Notes
        );
        CafeteriaServiceEventEnvelope serviceEvent = await store.AppendAsync
        (
              operationId
            , CafeteriaServiceEventKind.Stipend
            , request.CustomerId
            , processedAt
            , payload
            , ct
        );

        return Results.Created($"/api/events/{serviceEvent.Sequence}", serviceEvent);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Forbid();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(exception.Message);
    }
}).RequireAuthorization("CafeteriaManagement");

app.MapGet("/api/management/funding-policy", async (CafeteriaFundingPolicyStore fundingPolicy, CancellationToken ct) =>
{
    return Results.Ok(await fundingPolicy.GetFundingPolicyAsync(ct));
}).RequireAuthorization("CafeteriaManagement");

app.MapPut("/api/management/funding-policy", async (FundingPolicyRequest request, HttpContext http, FundingPolicyManagementService fundingPolicy, CancellationToken ct) =>
{
    if (request.DailyCreditLimitRate <= 0m || request.DailyCompanySubsidyRate <= 0m || request.DailyCompanySubsidyRate > request.DailyCreditLimitRate)
    {
        return Results.BadRequest("Funding rates must be positive and the company subsidy rate cannot exceed the credit-limit rate.");
    }

    string actor = RequireActor(http);
    return Results.Ok
    (
        await fundingPolicy.SetFundingPolicyAsync
        (
              request.DailyCreditLimitRate
            , request.DailyCompanySubsidyRate
            , actor
            , ct
        )
    );
}).RequireAuthorization("CafeteriaManagement");



app.MapGet("/api/management/payroll-report", async (int year, int month, PayrollDeductionReportSource payroll, CancellationToken ct) =>
{
    return Results.Ok(await payroll.BuildAsync(year, month, ct));
}).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/management/payroll-report/send", async (int year, int month, bool? force, ScheduledEmailWorker worker, CancellationToken ct) =>
{
    PayrollDeductionReportPackage report = await worker.SendPayrollReportAsync(year, month, force ?? false, ct);
    return Results.Ok(new
    {
          report.Year
        , report.Month
        , report.EmployeesWithAmountDue
        , report.TotalEmployeeAmountDue
        , Forced = force ?? false
    });
}).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/month-opening/run", async (int? year, int? month, MonthOpeningWorker worker, CancellationToken ct) =>
{
    var now=DateTime.Now; var y=year ?? now.Year; var m=month ?? now.Month;
    if(m is <1 or >12) return Results.BadRequest("Month must be 1-12.");
    return Results.Ok(new { created=await worker.EnsureMonthAsync(y,m,ct), year=y, month=m });
}).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/month-opening/recalculate", async (int? year, int? month, HttpContext http, MonthOpeningWorker worker, CancellationToken ct) =>
{
    DateTime now = DateTime.Now;
    int selectedYear = year ?? now.Year;
    int selectedMonth = month ?? now.Month;

    if (selectedMonth is < 1 or > 12)
    {
        return Results.BadRequest("Month must be 1-12.");
    }

    try
    {
        MonthRecalculationResult result = await worker.RecalculateMonthAsync
        (
              selectedYear
            , selectedMonth
            , RequireActor(http)
            , ct
        );

        return Results.Ok(result);
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
}).RequireAuthorization("CafeteriaManagement");

if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/development/reset", async (HttpContext http, DevelopmentDataResetService reset, CancellationToken ct) =>
    {
        string actor = RequireActor(http);
        DevelopmentResetResult result = await reset.ResetAsync(actor, ct);
        return Results.Ok(result);
    }).RequireAuthorization("CafeteriaManagement");
}

app.MapGet("/api/ffp/customers", async (IWorkforceReferenceSource source,CustomerCreditPolicyStore policies,CancellationToken ct)=>
{
    var customers=await source.GetCustomersAsync(ct);
    var limits=await policies.GetAllAdditionalCreditLimitsAsync(ct);
    return Results.Ok(customers.Select(x=>x with { AdditionalCreditLimit=limits.GetValueOrDefault(x.CustomerId,0m) }).ToArray());
});
app.MapGet("/api/ffp/holidays", async (int year,int month,IWorkforceReferenceSource source,CancellationToken ct)=>Results.Ok(await source.GetHolidaysAsync(year,month,ct)));
app.MapGet("/api/ffp/accounts", async (IExternalAccountingGateway gateway,CancellationToken ct)=>Results.Ok(await gateway.GetAccountSummariesAsync(ct)));
app.MapPost("/api/ffp/ledger", async (ExternalLedgerEntry entry, IExternalAccountingGateway gateway, ServiceEventStore store, CancellationToken ct) =>
{
    DateTimeOffset? latestResetAt = await store.GetLatestDevelopmentResetAtAsync(ct);

    if (app.Environment.IsDevelopment() && latestResetAt is DateTimeOffset resetAt && entry.OccurredAt <= resetAt)
    {
        return Results.Ok(new { ignored = true, reason = "Entry predates the latest Development reset." });
    }

    await gateway.PublishLedgerEntryAsync(entry, ct);
    return Results.Ok();
});

await app.RunAsync();

static string RequireActor(HttpContext http)
{
    var name=http.User.Identity?.Name;
    if(http.User.Identity?.IsAuthenticated!=true || string.IsNullOrWhiteSpace(name))
        throw new UnauthorizedAccessException("Authenticated management identity is required.");
    return name.Trim();
}

public sealed record FundingPolicyRequest(decimal DailyCreditLimitRate, decimal DailyCompanySubsidyRate);
