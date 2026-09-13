using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractPublisher(IConfiguration configuration, IWebHostEnvironment environment, ILogger<ConfigurationContractPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";
    private static readonly TimeSpan retryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? baseUrl = configuration["AegisConfiguration:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) { logger.LogInformation("Aegis.Configuration publication is disabled because AegisConfiguration:BaseUrl is not configured."); return; }
        string path = configuration["AegisConfiguration:ContractPath"] ?? Path.Combine(environment.ContentRootPath, "configuration", "Aegis.Cafeteria.Services.configuration-contract.json");
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryPublishAsync(baseUrl, path, stoppingToken)) return;
            try { await Task.Delay(retryDelay, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryPublishAsync(string baseUrl, string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) { logger.LogWarning("Aegis.Configuration contract was not found at {ContractPath}; Cafeteria Services remains operational and will retry publication.", path); return false; }
            ISecretProvider secrets = CommonSecretProviderFactory.Create(configuration);
            string? key = await secrets.GetAsync($"configuration/registration/{ApplicationId}", cancellationToken);
            if (string.IsNullOrWhiteSpace(key)) { logger.LogWarning("Aegis.Configuration registration credential is unavailable; Cafeteria Services remains operational and will retry publication."); return false; }
            JsonNode contract = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) ?? throw new InvalidDataException("Configuration contract is empty.");
            contract["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
            contract["siteId"] = NullIfBlank(configuration["Site:Id"]);
            contract["instanceId"] = NullIfBlank(configuration["Service:Identity"]);
            JsonArray requirements = contract["requirements"]?.AsArray() ?? throw new InvalidDataException("Configuration contract has no requirements array.");
            foreach (JsonNode? node in requirements)
            {
                if (node is not JsonObject requirement) continue;
                string id = requirement["id"]?.GetValue<string>() ?? string.Empty;
                requirement["isConfigured"] = id switch
                {
                    "central-database" => HasValue(configuration["ServicePersistence:ConnectionString"]),
                    "openbao-url" => HasAnyValue(configuration["Secrets:OpenBao:Address"], configuration["OpenBao:Address"], Environment.GetEnvironmentVariable("OPENBAO_ADDR"), Environment.GetEnvironmentVariable("VAULT_ADDR")),
                    "openbao-credentials" => HasAnyValue(configuration["Secrets:OpenBao:Token"], configuration["OpenBao:Token"], configuration["Secrets:OpenBao:RoleId"], Environment.GetEnvironmentVariable("OPENBAO_TOKEN"), Environment.GetEnvironmentVariable("VAULT_TOKEN")),
                    "site-id" => HasValue(configuration["Site:Id"]),
                    "service-identity" => HasValue(configuration["Service:Identity"]),
                    _ => false
                };
            }
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/contracts/register");
            request.Headers.TryAddWithoutValidation("X-Configuration-Registration-Key", key);
            request.Content = new StringContent(contract.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("Aegis.Configuration rejected Cafeteria Services publication with HTTP {StatusCode}; service remains operational and will retry.", (int)response.StatusCode); return false; }
            logger.LogInformation("Aegis.Cafeteria.Services configuration requirements and configured state are registered with Aegis.Configuration.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogWarning(exception, "Unable to publish Aegis.Cafeteria.Services configuration requirements; service remains operational and will retry publication."); return false; }
    }
    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool HasAnyValue(params string?[] values) => values.Any(HasValue);
    private static string? NullIfBlank(string? value) => HasValue(value) ? value : null;
}
