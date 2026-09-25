namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;
using System.Net.Http.Json;

public sealed class IntegrationPlaneClient :
      ICafeteriaPolicyProvider
    , IEmployeeFactSource
    , IEmployeeIdScanPublisher
{
    private readonly HttpClient http;
    private readonly Dictionary<(int Year, int Month), CafeteriaPolicySnapshot> policyCache = [];

    public IntegrationPlaneClient(
        HttpClient http,
        IntegrationPluginOptions options)
    {
        if (!options.Enabled)
            throw new InvalidOperationException("Cafeteria integration plugin is disabled.");

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri))
            throw new InvalidOperationException("IntegrationPlugin:BaseUrl must be an absolute URL.");

        this.http = http;
        this.http.BaseAddress = new Uri(baseUri.ToString().TrimEnd('/') + "/");
        this.http.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
    }

    public async Task<CafeteriaPolicySnapshot> GetPolicyAsync(
        int year,
        int month,
        bool forceReload = false,
        CancellationToken cancellationToken = default)
    {
        if (!forceReload && this.policyCache.TryGetValue((year, month), out CafeteriaPolicySnapshot? cached))
            return cached;

        CafeteriaPolicySnapshot? policy = await this.http.GetFromJsonAsync<CafeteriaPolicySnapshot>(
            $"api/cafeteria/policy?year={year}&month={month}",
            cancellationToken);

        if (policy is null)
            throw new InvalidOperationException("The integration plane returned no cafeteria policy.");

        policy.Validate();

        if (policy.Year != year || policy.Month != month)
            throw new InvalidOperationException("The integration plane returned policy for the wrong period.");

        this.policyCache[(year, month)] = policy;
        return policy;
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
