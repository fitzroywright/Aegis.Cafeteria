using Aegis.Cafeteria.Management;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
TimeSpan sessionLifetime = ManagementHost.Configure(builder);

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

ManagementEndpoints.Map(app, sessionLifetime);

await app.RunAsync();
