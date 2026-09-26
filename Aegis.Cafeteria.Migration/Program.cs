using Aegis.Cafeteria.Application.Integration;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Loader;

MigrationOptions options = MigrationOptions.Parse(args);
ILegacyCafeteriaMigrationSource source = LoadMigrationSource(options.PluginPath);
string connectionString = options.ResolveConnectionString();

if (options.Mode == MigrationMode.ProductBootstrap)
{
    await RunProductBootstrapAsync(options, source, connectionString);
    return;
}

DateTimeOffset throughAt = DateTimeOffset.Now;
Console.WriteLine($"Aegis.Cafeteria legacy migration - {options.Year:D4}-{options.Month:D2}");
Console.WriteLine($"Source provider: {Path.GetFileName(options.PluginPath)}");
Console.WriteLine($"Mode: {(options.Execute ? "EXECUTE" : "DRY RUN")}");

LegacyCafeteriaMonthSnapshot snapshot = await source.ReadMonthAsync(
    connectionString,
    options.Year,
    options.Month,
    throughAt);

Console.WriteLine($"Through: {snapshot.ThroughAt:O}");
Console.WriteLine($"Transactions: {snapshot.Transactions.Count}");
Console.WriteLine($"Accounts: {snapshot.Accounts.Count}");
Console.WriteLine($"Net transaction value: {snapshot.Transactions.Sum(x => x.ScanValue):N2}");
Console.WriteLine($"Account credit limit total: {snapshot.Accounts.Sum(x => x.CreditLimit):N2}");
Console.WriteLine($"Account available balance total: {snapshot.Accounts.Sum(x => x.AvailableBalance):N2}");

foreach (IGrouping<int, LegacyCafeteriaTransactionSnapshot> group in snapshot.Transactions
    .GroupBy(x => x.ScanType)
    .OrderBy(x => x.Key))
{
    Console.WriteLine($"Source type {group.Key}: {group.Count()} transaction(s), {group.Sum(x => x.ScanValue):N2}");
}

if (!options.Execute)
{
    Console.WriteLine("Dry run complete. Re-run with --execute to submit the migration.");
    return;
}

using HttpClient http = CreateServicesClient(options.ServicesUrl);
using HttpResponseMessage response = await http.PostAsJsonAsync(
    "api/migration/legacy-month",
    snapshot);
string responseBody = await response.Content.ReadAsStringAsync();

if (!response.IsSuccessStatusCode)
{
    throw new InvalidOperationException(
        $"Migration endpoint returned HTTP {(int)response.StatusCode}: {responseBody}");
}

Console.WriteLine("Migration accepted by Cafeteria Services.");
Console.WriteLine(responseBody);

static async Task RunProductBootstrapAsync(
    MigrationOptions options,
    ILegacyCafeteriaMigrationSource source,
    string connectionString)
{
    Console.WriteLine("Aegis.Cafeteria product catalog migration");
    Console.WriteLine($"Source provider: {Path.GetFileName(options.PluginPath)}");
    Console.WriteLine($"Services: {options.ServicesUrl}");
    Console.WriteLine($"Mode: {(options.Execute ? "EXECUTE" : "DRY RUN")}");

    IReadOnlyList<LegacyCafeteriaProductSnapshot> products =
        await source.ReadProductsAsync(connectionString);

    Console.WriteLine($"Source products: {products.Count}");

    if (!options.Execute)
    {
        foreach (LegacyCafeteriaProductSnapshot product in products)
            Console.WriteLine($"{product.ProductId}: {product.Code} | {product.Name} | {product.Price:N2}");

        Console.WriteLine("Dry run complete. Re-run with --execute to submit the product migration.");
        return;
    }

    using HttpClient http = CreateServicesClient(options.ServicesUrl);
    using HttpResponseMessage response = await http.PostAsJsonAsync(
        "api/migration/products",
        new { Products = products });

    string responseBody = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            $"Product migration endpoint returned HTTP {(int)response.StatusCode}: {responseBody}");
    }

    Console.WriteLine("Product migration accepted by Cafeteria Services.");
    Console.WriteLine(responseBody);
}

static HttpClient CreateServicesClient(string servicesUrl)
{
    HttpClientHandler handler = new()
    {
        UseDefaultCredentials = true
    };

    return new HttpClient(handler)
    {
        BaseAddress = new Uri(servicesUrl.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromMinutes(5)
    };
}

static ILegacyCafeteriaMigrationSource LoadMigrationSource(string configuredPath)
{
    string path = Path.IsPathRooted(configuredPath)
        ? Path.GetFullPath(configuredPath)
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredPath));

    if (!File.Exists(path))
        throw new FileNotFoundException("Configured Cafeteria migration integration plugin was not found.", path);

    Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    Type contract = typeof(ILegacyCafeteriaMigrationSource);
    Type[] matches = assembly.GetTypes()
        .Where(type => type.IsClass && !type.IsAbstract && type.IsPublic && contract.IsAssignableFrom(type))
        .ToArray();

    if (matches.Length != 1)
    {
        throw new InvalidOperationException(
            $"Integration plugin must contain exactly one {contract.FullName} implementation; found {matches.Length}.");
    }

    return (ILegacyCafeteriaMigrationSource)(
        Activator.CreateInstance(matches[0])
        ?? throw new InvalidOperationException($"Could not create migration source {matches[0].FullName}."));
}

internal enum MigrationMode
{
    LegacyMonth,
    ProductBootstrap
}

internal sealed record MigrationOptions(
    MigrationMode Mode,
    string? ConnectionString,
    string? Server,
    string? Database,
    string ServicesUrl,
    string PluginPath,
    int Year,
    int Month,
    bool Execute,
    bool Encrypt,
    bool TrustServerCertificate)
{
    internal static MigrationOptions Parse(string[] args)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
                continue;

            string key = argument[2..];
            string? value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : null;
            values[key] = value;
        }

        MigrationMode mode = values.ContainsKey("product-bootstrap")
            ? MigrationMode.ProductBootstrap
            : MigrationMode.LegacyMonth;

        string servicesUrl = Required(values, "services-url");
        string pluginPath = values.TryGetValue("plugin", out string? plugin)
            && !string.IsNullOrWhiteSpace(plugin)
                ? plugin.Trim()
                : Path.Combine("plugins", "Aegis.Cafeteria.Plugin.Integration.dll");

        int year = values.TryGetValue("year", out string? yearText) && int.TryParse(yearText, out int parsedYear)
            ? parsedYear
            : DateTime.Today.Year;
        int month = values.TryGetValue("month", out string? monthText) && int.TryParse(monthText, out int parsedMonth)
            ? parsedMonth
            : DateTime.Today.Month;

        if (year is < 2000 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        values.TryGetValue("connection-string", out string? connectionString);
        values.TryGetValue("server", out string? server);
        values.TryGetValue("database", out string? database);

        if (string.IsNullOrWhiteSpace(connectionString)
            && (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(database)))
        {
            throw new ArgumentException(
                "Specify --connection-string, or provide both --server and --database.");
        }

        return new MigrationOptions(
            mode,
            connectionString?.Trim(),
            server?.Trim(),
            database?.Trim(),
            servicesUrl,
            pluginPath,
            year,
            month,
            values.ContainsKey("execute"),
            !values.ContainsKey("no-encrypt"),
            values.ContainsKey("trust-server-certificate"));
    }

    internal string ResolveConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
            return ConnectionString;

        string encrypt = Encrypt ? "True" : "False";
        string trust = TrustServerCertificate ? "True" : "False";
        return $"Integrated Security=SSPI;Pooling=false;Data Source={Server};Initial Catalog={Database};Encrypt={encrypt};TrustServerCertificate={trust}";
    }

    private static string Required(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"Missing required --{key} value.");
}
