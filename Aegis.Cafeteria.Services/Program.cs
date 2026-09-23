using Common.Diagnostics;
using Common.Registration;
using Aegis.Cafeteria.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CafeteriaServicesHost.Configure(builder);
string operationsLogInstanceId = builder.Configuration["Service:Identity"]?.Trim() ?? Environment.MachineName;
string operationsLogIdentityFile = builder.Configuration["Aegis:Registration:IdentityFile"]
    ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aegis", "Cafeteria.Services", "registration-identity.json")
        : "/var/lib/aegis/cafeteria-services/registration-identity.json");
builder.Services.AddAegisOperationsLogging(builder.Configuration, "Aegis.Cafeteria.Services", operationsLogInstanceId, operationsLogIdentityFile, builder.Environment.EnvironmentName);
builder.Services.AddHostedService<Aegis.Cafeteria.Services.OperationsTelemetryPublisher>();

WebApplication app = builder.Build();
app.MapDiagnosticLevelRuntime();
CafeteriaServicesPipeline.ConfigureLogging(app);
await CafeteriaServiceDatabase.InitializeAsync(app, app.Lifetime.ApplicationStopping);
CafeteriaServicesPipeline.Configure(app);

EngineeringEndpoints.Map(app);
PosEndpoints.Map(app);
CustomerManagementEndpoints.Map(app);
FinancialManagementEndpoints.Map(app);
ImportToolsEndpoints.Map(app);
MonthOpeningEndpoints.Map(app);
FfpEndpoints.Map(app);

await app.RunAsync();
