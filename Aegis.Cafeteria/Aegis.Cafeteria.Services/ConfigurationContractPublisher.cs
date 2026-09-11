using System.Text;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractPublisher(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ConfigurationContractPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? baseUrl = configuration["AegisConfiguration:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogInformation("Aegis.Configuration contract publication is disabled because AegisConfiguration:BaseUrl is not configured.");
            return;
        }

        string path = configuration["AegisConfiguration:ContractPath"]
            ?? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "configuration", "Aegis.Cafeteria.configuration-contract.json"));

        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Aegis.Configuration contract was not found at {ContractPath}; Cafeteria will continue without publishing it.", path);
                return;
            }

            ISecretProvider secrets = CommonSecretProviderFactory.Create(configuration);
            string? key = await secrets.GetAsync("configuration/registration/Aegis.Cafeteria", stoppingToken);
            if (string.IsNullOrWhiteSpace(key))
            {
                logger.LogWarning("Aegis.Configuration registration credential is unavailable; Cafeteria will continue without publishing its contract.");
                return;
            }

            string json = await File.ReadAllTextAsync(path, stoppingToken);
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/contracts/register");
            request.Headers.TryAddWithoutValidation("X-Configuration-Registration-Key", key);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request, stoppingToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Aegis.Configuration rejected Cafeteria contract publication with HTTP {StatusCode}; Cafeteria remains operational.", (int)response.StatusCode);
                return;
            }

            logger.LogInformation("Published Aegis.Cafeteria configuration contract to Aegis.Configuration.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to publish Aegis.Cafeteria configuration contract; Cafeteria remains operational.");
        }
    }
}
