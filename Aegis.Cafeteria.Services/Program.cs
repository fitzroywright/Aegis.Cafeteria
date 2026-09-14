using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Logging;
using Aegis.Cafeteria.Infrastructure.Secrets;
using Aegis.Cafeteria.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Development;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Realtime;
using Aegis.Cafeteria.Services.Workers;

using Aegis.Engineering.Diagnostics;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "Aegis.Cafeteria.Services");
builder.Configuration.AddEnvironmentVariables();

bool developmentDataEnabled = builder.Configuration.GetSection("DevelopmentData").GetValue("Enabled", false);
if (developmentDataEnabled && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("DevelopmentData can only be enabled in the Development environment.");

IConfigurationSection ffpSection = builder.Configuration.GetSection("FfpIntegration");
FfpIntegrationOptions configuredFfp = ffpSection.Get<FfpIntegrationOptions>() ?? new FfpIntegrationOptions();
FfpIntegrationOptions ffpIntegration = BuildTrustedFfpOptions(ffpSection, configuredFfp, developmentDataEnabled);

var management = builder.Configuration.GetSection("ManagementApi").Get<ManagementApiOptions>() ?? new();
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

builder.Services.AddSingleton(ffpIntegration);
builder.Services.AddSingleton(management);
builder.Services.AddSingleton(monthOpening);
builder.Services.AddSingleton(scheduledEmail);
builder.Services.AddSingleton<MonthlyAllocationCalculator>();
if (developmentDataEnabled)
{
    builder.Services.AddSingleton<DevelopmentFfpGateway>();
    builder.Services.AddSingleton<DevelopmentLunchItemBootstrapSource>();
    builder.Services.AddSingleton<DevelopmentFfpMonthImportSource>();
    builder.Services.AddSingleton<IExternalAccountingGateway>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IWorkforceReferenceSource>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<ILunchItemBootstrapSource>(sp => sp.GetRequiredService<DevelopmentLunchItemBootstrapSource>());
    builder.Services.AddSingleton<IFfpMonthImportSource>(sp => sp.GetRequiredService<DevelopmentFfpMonthImportSource>());
    builder.Services.AddSingleton<IFundingPolicyCompatibilityGateway>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IMonthRecalculationGuard>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<IDevelopmentDataResetGateway>(sp => sp.GetRequiredService<DevelopmentFfpGateway>());
    builder.Services.AddSingleton<DevelopmentPayrollLedgerSource>();
    builder.Services.AddSingleton<IPayrollLedgerSource>(sp => sp.GetRequiredService<DevelopmentPayrollLedgerSource>());
}
else
{
    builder.Services.AddSingleton<FfpSqlApplicationGateway>();
    builder.Services.AddSingleton<FfpLunchItemBootstrapSource>();
    builder.Services.AddSingleton<FfpMonthImportSource>();
    builder.Services.AddSingleton<IExternalAccountingGateway>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<IWorkforceReferenceSource>(sp => sp.GetRequiredService<FfpSqlApplicationGateway>());
    builder.Services.AddSingleton<ILunchItemBootstrapSource>(sp => sp.GetRequiredService<FfpLunchItemBootstrapSource>());
    builder.Services.AddSingleton<IFfpMonthImportSource>(sp => sp.GetRequiredService<FfpMonthImportSource>());
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
    builder.Services.AddCors
    (
        options => options.AddPolicy
        (
              "DevelopmentManagement"
            , policy => policy
                .WithOrigins("http://localhost:5090")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
        )
    );
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
builder.Services.AddSingleton<EngineeringDiagnosticRunStore>();
builder.Services.AddSingleton<CafeteriaEngineeringDiagnosticService>();

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CafeteriaPos", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("aegis_authentication_source", "Common.Security");
    });

    options.AddPolicy("CafeteriaManagement", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            management.AuthorizedGroups.Length == 0 || management.AuthorizedGroups.Any(context.User.IsInRole));
    });

    options.AddPolicy("EngineeringDiagnostics", policy =>
    {
        policy.RequireAuthenticatedUser();

        if (!builder.Environment.IsDevelopment())
        {
            policy.RequireAssertion(context =>
                management.AuthorizedGroups.Length == 0 || management.AuthorizedGroups.Any(context.User.IsInRole));
        }
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
CafeteriaLoggingOptions loggingOptions = app.Configuration.GetSection("Logging").Get<CafeteriaLoggingOptions>()
    ?? new CafeteriaLoggingOptions();
ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddCafeteriaProviders(loggingOptions);

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
        );
        CREATE TABLE IF NOT EXISTS CustomerNotificationPreferences (
              CustomerId INTEGER NOT NULL CONSTRAINT PK_CustomerNotificationPreferences PRIMARY KEY
            , PreferenceAsked INTEGER NOT NULL
            , WeeklySummaryEnabled INTEGER NOT NULL
            , EmailAddress TEXT NULL
            , UpdatedAt TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS ManagerReportSettings (
              Id INTEGER NOT NULL CONSTRAINT PK_ManagerReportSettings PRIMARY KEY
            , WeeklySummaryEnabled INTEGER NOT NULL
            , EmailAddress TEXT NULL
            , WeeklySummaryDay INTEGER NOT NULL
            , WeeklySummaryHourLocal INTEGER NOT NULL
            , UpdatedAt TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS OutboundEmails (
              Id TEXT NOT NULL CONSTRAINT PK_OutboundEmails PRIMARY KEY
            , DispatchKey TEXT NOT NULL
            , Recipient TEXT NOT NULL
            , Subject TEXT NOT NULL
            , Body TEXT NOT NULL
            , AttemptCount INTEGER NOT NULL
            , CreatedAt TEXT NOT NULL
            , NextAttemptAt TEXT NOT NULL
            , LastAttemptAt TEXT NULL
            , SentAt TEXT NULL
            , MessageId TEXT NULL
            , LastError TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboundEmails_DispatchKey ON OutboundEmails (DispatchKey);
        CREATE INDEX IF NOT EXISTS IX_OutboundEmails_SentAt_NextAttemptAt ON OutboundEmails (SentAt, NextAttemptAt);"
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
    app.UseCors("DevelopmentManagement");
}

app.UseAuthentication();
app.UseAuthorization();
app.UseCafeteriaAuditTrail();
AuditTrailEndpoints.Map(app);

app.MapGet("/health", () => Results.Ok(new { status="running", application="Aegis.Cafeteria.Services", timeUtc=DateTimeOffset.UtcNow }));

app.MapGet("/engineering/login", (HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated == true)
    {
        return Results.Redirect("/engineering/diagnostics");
    }

    return Results.Content(EngineeringDiagnosticsLoginPage.Html, "text/html");
});

app.MapGet("/engineering/signin", async (HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated == true)
    {
        http.Response.Redirect("/engineering/diagnostics");
        return;
    }

    await http.ChallengeAsync(NegotiateDefaults.AuthenticationScheme);
});

app.MapGet("/engineering/diagnostics", (HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated != true)
    {
        return Results.Redirect("/engineering/login");
    }

    return Results.Content(LcarsDiagnosticsDashboard.Html, "text/html");
}).RequireAuthorization("EngineeringDiagnostics");

app.MapGet("/api/engineering/diagnostics/runs", async (int? take, EngineeringDiagnosticRunStore store, CancellationToken ct) =>
{
    IReadOnlyList<EngineeringDiagnosticRun> runs = await store.GetRecentAsync(take ?? 25, ct);
    return Results.Ok(runs);
}).RequireAuthorization("EngineeringDiagnostics");

app.MapGet("/api/engineering/diagnostics/runs/{runId:guid}", async (Guid runId, EngineeringDiagnosticRunStore store, CancellationToken ct) =>
{
    EngineeringDiagnosticRun? run = await store.GetAsync(runId, ct);
    return run is null ? Results.NotFound() : Results.Ok(run);
}).RequireAuthorization("EngineeringDiagnostics");

app.MapPost("/api/engineering/diagnostics/runs/{runId:guid}/resolve", async (Guid runId, EngineeringDiagnosticResolutionRequest request, HttpContext http, EngineeringDiagnosticRunStore store, CancellationToken ct) =>
{
    try
    {
        EngineeringDiagnosticRun? run = await store.ResolveAsync(runId, RequireActor(http), request.Resolution, ct);
        return run is null ? Results.NotFound() : Results.Ok(run);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}).RequireAuthorization("EngineeringDiagnostics");

app.MapPost("/api/engineering/diagnostics/run", async (EngineeringDiagnosticRunRequest request, HttpContext http, CafeteriaEngineeringDiagnosticService diagnostics, CancellationToken ct) =>
{
    try
    {
        EngineeringDiagnosticRun run = await diagnostics.RunAsync(request.Level, RequireActor(http), request.Reason, ct);
        return Results.Ok(run);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
}).RequireAuthorization("EngineeringDiagnostics");

app.MapHub<ManagementEventsHub>("/hubs/cafeteria").RequireAuthorization("CafeteriaPos");
app.MapGet("/api/pos/events", async (long afterSequence, int? take, ServiceEventStore store, CancellationToken ct)
    => Results.Ok(await store.GetAfterAsync(Math.Max(0,afterSequence), take ?? 100, ct)))
    .RequireAuthorization("CafeteriaPos");
EmailEndpoints.Map(app);

app.MapGet("/api/management/customers", async (string? search, int? take, IWorkforceReferenceSource workforce, CancellationToken ct) =>
{
    string term = (search ?? string.Empty).Trim();
    int limit = Math.Clamp(take ?? 25, 1, 100);

    if (term.Length < 2)
    {
        return Results.Ok(Array.Empty<object>());
    }

    IReadOnlyList<ExternalCustomerSnapshot> employees = await workforce.GetCustomersAsync(ct);
    string normalized = term.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    object[] matches = employees
        .Where(employee => employee.IsEligibleForCafeteriaCredit)
        .Where(employee =>
               employee.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(employee.Code) && employee.Code.Contains(term, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(employee.Trn) && employee.Trn.Replace(" ", string.Empty, StringComparison.Ordinal).Contains(normalized, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(employee => employee.Name)
        .Take(limit)
        .Select(employee => (object)new
        {
              employee.CustomerId
            , employee.Name
            , employee.Code
            , employee.Trn
            , employee.Department
        })
        .ToArray();

    return Results.Ok(matches);
}).RequireAuthorization("CafeteriaManagement");

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
    catch(UnauthorizedAccessException){return Results.Forbid();}
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
        if (existing is not null) return Results.Ok(existing);
    }

    IReadOnlyList<ExternalCustomerSnapshot> employees = await workforce.GetCustomersAsync(ct);
    ExternalCustomerSnapshot? employee = employees.SingleOrDefault(candidate => candidate.CustomerId == request.CustomerId);
    if (employee is null) return Results.NotFound("Employee was not found in FFP.");
    if (!employee.IsEligibleForCafeteriaCredit) return Results.BadRequest("Employee is not eligible for cafeteria credit.");

    string actor = RequireActor(http);
    DateTimeOffset processedAt = request.ProcessedAt ?? DateTimeOffset.Now;
    int fundingYear = request.FundingYear ?? processedAt.Year;
    int fundingMonth = request.FundingMonth ?? processedAt.Month;

    if (fundingYear < 2000 || fundingYear > 9999 || fundingMonth is < 1 or > 12)
    {
        return Results.BadRequest("A valid stipend funding year and month are required.");
    }

    DateOnly processingMonth = new(processedAt.Year, processedAt.Month, 1);
    DateOnly fundingPeriod = new(fundingYear, fundingMonth, 1);
    if (fundingPeriod > processingMonth)
    {
        return Results.BadRequest("A stipend cannot be charged to a future funding period.");
    }

    try
    {
        CustomerAccountSnapshot account = await accountFactory.BuildAsync(request.CustomerId, fundingYear, fundingMonth, ct);
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
            , fundingYear
            , fundingMonth
        );

        if (request.CardReductionAmount != stipend.CardReduction)
        {
            return Results.BadRequest($"Card reduction must be {stipend.CardReduction:N2} for the employee's selected funding-period subsidy and cafeteria balance.");
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
            , stipend.FundingYear
            , stipend.FundingMonth
        );
        CafeteriaServiceEventEnvelope serviceEvent = await store.AppendAsync(operationId, CafeteriaServiceEventKind.Stipend, request.CustomerId, processedAt, payload, ct);
        return Results.Created($"/api/events/{serviceEvent.Sequence}", serviceEvent);
    }
    catch (UnauthorizedAccessException){return Results.Forbid();}
    catch (InvalidOperationException exception){return Results.Conflict(new { error = exception.Message });}
    catch (ArgumentException exception){return Results.BadRequest(exception.Message);}
}).RequireAuthorization("CafeteriaManagement");

app.MapGet("/api/management/funding-policy", async (CafeteriaFundingPolicyStore fundingPolicy, CancellationToken ct)
    => Results.Ok(await fundingPolicy.GetFundingPolicyAsync(ct))).RequireAuthorization("CafeteriaManagement");

app.MapPut("/api/management/funding-policy", async (FundingPolicyRequest request, HttpContext http, FundingPolicyManagementService fundingPolicy, CancellationToken ct) =>
{
    if (request.DailyCreditLimitRate <= 0m || request.DailyCompanySubsidyRate <= 0m || request.DailyCompanySubsidyRate > request.DailyCreditLimitRate)
        return Results.BadRequest("Funding rates must be positive and the company subsidy rate cannot exceed the credit-limit rate.");
    string actor = RequireActor(http);
    return Results.Ok(await fundingPolicy.SetFundingPolicyAsync(request.DailyCreditLimitRate, request.DailyCompanySubsidyRate, actor, ct));
}).RequireAuthorization("CafeteriaManagement");

app.MapGet("/api/management/payroll-report", async (int year, int month, PayrollDeductionReportSource payroll, CancellationToken ct)
    => Results.Ok(await payroll.BuildAsync(year, month, ct))).RequireAuthorization("CafeteriaManagement");

app.MapPost("/api/management/payroll-report/send", async (int year, int month, bool? force, ScheduledEmailWorker worker, CancellationToken ct) =>
{
    PayrollDeductionReportPackage report = await worker.SendPayrollReportAsync(year, month, force ?? false, ct);
    return Results.Ok(new { report.Year, report.Month, report.EmployeesWithAmountDue, report.TotalEmployeeAmountDue, Forced = force ?? false });
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
    if (selectedMonth is < 1 or > 12) return Results.BadRequest("Month must be 1-12.");
    try
    {
        MonthRecalculationResult result = await worker.RecalculateMonthAsync(selectedYear, selectedMonth, RequireActor(http), ct);
        return Results.Ok(result);
    }
    catch (InvalidOperationException exception){return Results.Conflict(new { error = exception.Message });}
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
}).RequireAuthorization("CafeteriaPos");
app.MapGet("/api/ffp/holidays", async (int year,int month,IWorkforceReferenceSource source,CancellationToken ct)=>Results.Ok(await source.GetHolidaysAsync(year,month,ct)))
    .RequireAuthorization("CafeteriaPos");
app.MapGet("/api/ffp/lunch-items", async (ILunchItemBootstrapSource source,CancellationToken ct)=>Results.Ok(await source.GetLunchItemsAsync(ct)))
    .RequireAuthorization("CafeteriaPos");
app.MapGet("/api/ffp/month-import", async (int year, int month, IFfpMonthImportSource source,CancellationToken ct)=>Results.Ok(await source.GetMonthAsync(year, month, ct)))
    .RequireAuthorization("CafeteriaPos");
app.MapGet("/api/ffp/accounts", async (IExternalAccountingGateway gateway,CancellationToken ct)=>Results.Ok(await gateway.GetAccountSummariesAsync(ct)))
    .RequireAuthorization("CafeteriaPos");
app.MapPost("/api/ffp/ledger", async (ExternalLedgerEntry entry, IExternalAccountingGateway gateway, ServiceEventStore store, CancellationToken ct) =>
{
    DateTimeOffset? latestResetAt = await store.GetLatestDevelopmentResetAtAsync(ct);
    if (app.Environment.IsDevelopment() && latestResetAt is DateTimeOffset resetAt && entry.OccurredAt <= resetAt)
        return Results.Ok(new { ignored = true, reason = "Entry predates the latest Development reset." });
    await gateway.PublishLedgerEntryAsync(entry, ct);
    return Results.Ok();
}).RequireAuthorization("CafeteriaPos");

await TryRegisterServicesWithConfigurationAsync(app.Configuration, app.Environment, serviceDb, ffpIntegration, app.Lifetime.ApplicationStopping);
await app.RunAsync();

static FfpIntegrationOptions BuildTrustedFfpOptions
(
      IConfigurationSection section
    , FfpIntegrationOptions configured
    , bool developmentDataEnabled
)
{
    if (developmentDataEnabled)
    {
        return configured;
    }

    if (!configured.Enabled)
    {
        throw new InvalidOperationException("Aegis.Cafeteria.Services requires FFP integration in production.");
    }

    string server = section["Server"]?.Trim() ?? string.Empty;
    string database = section["Database"]?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database))
    {
        throw new InvalidOperationException("FfpIntegration:Server and FfpIntegration:Database are required. Cafeteria Services uses Windows Integrated Security and does not accept stored SQL credentials.");
    }

    SqlConnectionStringBuilder connection = new()
    {
          DataSource = server
        , InitialCatalog = database
        , IntegratedSecurity = true
        , Encrypt = section.GetValue("Encrypt", true)
        , TrustServerCertificate = section.GetValue("TrustServerCertificate", false)
        , ApplicationName = "Aegis.Cafeteria.Services"
    };

    return new FfpIntegrationOptions
    {
          Enabled = configured.Enabled
        , ConnectionString = connection.ConnectionString
        , AllowWrites = configured.AllowWrites
        , RefreshIntervalMinutes = configured.RefreshIntervalMinutes
        , CreditLimitLunchItemCode = configured.CreditLimitLunchItemCode
        , SubsidyLunchItemCode = configured.SubsidyLunchItemCode
        , MonthlyAllocationScanType = configured.MonthlyAllocationScanType
        , PurchaseScanType = configured.PurchaseScanType
        , StipendScanType = configured.StipendScanType
        , RefundScanType = configured.RefundScanType
        , AdjustmentScanType = configured.AdjustmentScanType
    };
}

static string RequireActor(HttpContext http)
{
    return http.User.Identity?.Name ?? "UNKNOWN";
}

static async Task TryRegisterServicesWithConfigurationAsync(
    IConfiguration configuration,
    IHostEnvironment environment,
    string serviceDb,
    FfpIntegrationOptions ffp,
    CancellationToken cancellationToken)
{
    string? configurationUrl = configuration["Aegis:Configuration:Url"];
    if (string.IsNullOrWhiteSpace(configurationUrl)) return;

    IApplicationSecretProvider secrets = ApplicationSecretProviderFactory.Create(configuration, environment);
    string? registrationKey = await secrets.GetSecretAsync(ApplicationSecretNames.ConfigurationRegistrationKey, cancellationToken);
    if (string.IsNullOrWhiteSpace(registrationKey)) return;

    string? siteId = configuration["Deployment:SiteId"];
    string? servicesUrl = configuration["urls"];
    object contract = new
    {
        ApplicationId = "Cafeteria.Services",
        DisplayName = "Aegis Cafeteria Services",
        Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        SiteId = string.IsNullOrWhiteSpace(siteId) ? null : siteId.Trim(),
        InstanceId = Environment.MachineName,
        Description = "Central synchronization, management, reporting, and integration services for Aegis Cafeteria.",
        Requirements = new object[]
        {
            Requirement("service-database", "Service Database", "Configuration", true,
                "Stores central Cafeteria Services state.", "ServicePersistence:ConnectionString",
                !string.IsNullOrWhiteSpace(serviceDb), serviceDb, "Data Source=cafeteria.services.db"),
            Requirement("service-url", "Service URL", "Configuration", true,
                "Defines the address exposed by Cafeteria Services.", "urls",
                !string.IsNullOrWhiteSpace(servicesUrl), servicesUrl, "http://0.0.0.0:5088"),
            Requirement("ffp-integration", "FFP Integration", "ExternalService", true,
                "Connects Cafeteria Services to the FFP workforce and accounting data source.", "FfpIntegration:Enabled",
                ffp.Enabled, ffp.Enabled ? "Enabled" : "Disabled", "Enabled"),
            Requirement("configuration-url", "System Configuration URL", "ExternalService", true,
                "Identifies the System Configuration registry.", "Aegis:Configuration:Url",
                true, configurationUrl, "http://localhost:5101"),
            Requirement("diagnostics-url", "System Diagnostics URL", "ExternalService", false,
                "Identifies the System Diagnostics service.", "Aegis:Diagnostics:Url",
                !string.IsNullOrWhiteSpace(configuration["Aegis:Diagnostics:Url"]), configuration["Aegis:Diagnostics:Url"], "http://localhost:5102")
        }
    };

    try
    {
        using HttpClient client = new()
        {
            BaseAddress = new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(3)
        };
        client.DefaultRequestHeaders.Add("X-Aegis-Registration-Key", registrationKey);
        using HttpResponseMessage response = await client.PostAsJsonAsync("api/contracts/register", contract, cancellationToken);
        _ = response.IsSuccessStatusCode;
    }
    catch (Exception) when (!cancellationToken.IsCancellationRequested)
    {
        // Registration is best-effort and must not become a service startup dependency.
    }
}

static object Requirement(
    string id,
    string displayName,
    string kind,
    bool required,
    string purpose,
    string configurationKey,
    bool isConfigured,
    string? safeDisplayValue,
    string example)
    => new
    {
        Id = id,
        DisplayName = displayName,
        Kind = kind,
        Required = required,
        Purpose = purpose,
        ConfigurationKey = configurationKey,
        IsConfigured = isConfigured,
        SafeDisplayValue = string.IsNullOrWhiteSpace(safeDisplayValue) ? null : safeDisplayValue.Trim(),
        Example = example,
        Sensitive = false
    };
