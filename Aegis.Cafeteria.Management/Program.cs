using Common.Diagnostics;
using Aegis.Cafeteria.Management;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
TimeSpan sessionLifetime = ManagementHost.Configure(builder);

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

ManagementEndpoints.Map(app, sessionLifetime);
app.MapAegisHealth("Aegis.Cafeteria.Management");

await app.RunAsync();
