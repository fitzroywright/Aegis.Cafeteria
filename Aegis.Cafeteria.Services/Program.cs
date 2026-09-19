using Aegis.Cafeteria.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CafeteriaServicesHost.Configure(builder);
builder.Services.AddHostedService<Aegis.Cafeteria.Services.OperationsTelemetryPublisher>();

WebApplication app = builder.Build();
CafeteriaServicesPipeline.ConfigureLogging(app);
await CafeteriaServiceDatabase.InitializeAsync(app, app.Lifetime.ApplicationStopping);
CafeteriaServicesPipeline.Configure(app);

EngineeringEndpoints.Map(app);
PosEndpoints.Map(app);
CustomerManagementEndpoints.Map(app);
FinancialManagementEndpoints.Map(app);
MonthOpeningEndpoints.Map(app);
FfpEndpoints.Map(app);

await app.RunAsync();
