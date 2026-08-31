using Microsoft.AspNetCore.Authentication.Negotiate;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet
(
      "/api/config"
    , (IConfiguration configuration) => Results.Ok
    (
        new
        {
            servicesBaseUrl = configuration["CafeteriaServices:BaseUrl"] ?? "http://localhost:5088"
        }
    )
).RequireAuthorization();

app.MapFallbackToFile("index.html").RequireAuthorization();

await app.RunAsync();
