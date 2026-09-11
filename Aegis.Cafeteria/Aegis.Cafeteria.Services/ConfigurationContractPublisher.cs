using System.Text;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractPublisher(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ConfigurationContractPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan retryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? baseUrl = configuration["AegisConfiguration:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            logger.LogInformation("Aegis.Configuration contract publication is disabled because AegisConfiguration:BaseUrl is not configured.");
            return;
        }

        string path = configuration["AegisConfiguration:ContractPath"]
            ?? Path.Combine(environment.ContentRootPath, "configuration", "Aegis.Cafeteria.configuration-contract.json");

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryPublishAsync(baseUrl, path, stoppingToken))
            {
                return;
            }

            try
            {
                await Task.Delay(retryDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<bool> TryPublishAsync(string baseUrl, string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                logger.LogWarning("Aegis.Configuration contract was not found at {ContractPath}; Cafeteria remains operational and will retry publication.", path);
                return false;
            }

            ISecretProvider secrets = CommonSecretProviderFactory.Create(configuration);
            string? key = await secrets.GetAsync("configuration/registration/Aegis.Cafeteria", cancellationToken);
            if (string.IsNullOrWhiteSpace(key))
            {
                logger.LogWarning("Aegis.Configuration registration credential is unavailable; Cafeteria remains operational and will retry publication.");
                return false;
            }

            string json = await File.ReadAllTextAsync(path, cancellationToken);
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/contracts/register");
            request.Headers.TryAddWithoutValidation("X-Configuration-Registration-Key", key);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Aegis.Configuration rejected Cafeteria contract publication with HTTP {StatusCode}; Cafeteria remains operational and will retry publication.", (int)response.StatusCode);
                return false;
            }

            logger.LogInformation("Aegis.Cafeteria configuration requirements are registered with Aegis.Configuration.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to publish Aegis.Cafeteria configuration requirements; Cafeteria remains operational and will retry publication.");
            return false;
        }
    }
}
