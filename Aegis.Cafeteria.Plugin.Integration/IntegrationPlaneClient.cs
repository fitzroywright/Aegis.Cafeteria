namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

public sealed class IntegrationPlaneClient :
      ICafeteriaPolicyProvider
    , IEmployeeFactSource
    , IEmployeeIdScanPublisher
{
    private readonly HttpClient http;
    private readonly Dictionary<(int Year, int Month), CafeteriaPolicySnapshot> policyCache = [];
    private readonly ICafeteriaPolicyCache durablePolicyCache;

    public IntegrationPlaneClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ICafeteriaPolicyCache durablePolicyCache)
    {
        this.durablePolicyCache = durablePolicyCache;
        IConfigurationSection section = configuration.GetSection("IntegrationPlugin");

        if (!section.GetValue("Enabled", true))
            throw new InvalidOperationException("Cafeteria integration plugin is disabled.");

        string baseUrl = section["BaseUrl"]?.Trim() ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
            throw new InvalidOperationException("IntegrationPlugin:BaseUrl must be an absolute URL.");

        int timeoutSeconds = Math.Clamp(
            section.GetValue("TimeoutSeconds", 30),
            5,
            120);

        this.http = httpClientFactory.CreateClient("CafeteriaIntegrationPlane");
        this.http.BaseAddress = new Uri(baseUri.ToString().TrimEnd('/') + "/");
        this.http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    public async Task<CafeteriaPolicySnapshot> GetPolicyAsync(
        int year,
        int month,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceReload && this.policyCache.TryGetValue((year, month), out CafeteriaPolicySnapshot? memoryCached))
            return memoryCached;

        try
        {
            CafeteriaPolicySnapshot? policy = await this.http.GetFromJsonAsync<CafeteriaPolicySnapshot>(
                $"api/cafeteria/policy?year={year}&month={month}",
                cancellationToken);

            if (policy is null)
                throw new InvalidOperationException("The integration plane returned no cafeteria policy.");

            policy.Validate();

            if (policy.Year != year || policy.Month != month)
                throw new InvalidOperationException("The integration plane returned policy for the wrong period.");

            this.policyCache[(year, month)] = policy;
            await this.durablePolicyCache.SaveAsync(policy, cancellationToken);
            return policy;
        }
        catch when (!forceReload && !cancellationToken.IsCancellationRequested)
        {
            CafeteriaPolicySnapshot? cached =
                await this.durablePolicyCache.GetAsync(
                    year,
                    month,
                    cancellationToken);

            if (cached is null)
                throw;

            this.policyCache[(year, month)] = cached;
            return cached;
        }
    }

    public async Task<EmployeeFactChangeSet> GetChangesAsync(
        string? afterVersion,
        CancellationToken cancellationToken = default)
    {
        string path = "api/cafeteria/employees/changes";
        if (!string.IsNullOrWhiteSpace(afterVersion))
            path += $"?afterVersion={Uri.EscapeDataString(afterVersion)}";

        EmployeeFactChangeSet? result = await this.http.GetFromJsonAsync<EmployeeFactChangeSet>(
            path,
            cancellationToken);

        if (result is null || string.IsNullOrWhiteSpace(result.Version))
            throw new InvalidOperationException("The integration plane returned an invalid employee change set.");

        return result;
    }

    public async Task PublishAsync(
        CafeteriaOutboundTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await this.http.PostAsJsonAsync(
            "api/cafeteria/employee-id-scans",
            transaction,
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
