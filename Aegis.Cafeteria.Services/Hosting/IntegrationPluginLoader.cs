namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Application.Integration;
using System.Reflection;
using System.Runtime.Loader;

internal static class IntegrationPluginLoader
{
    private static readonly Type[] RequiredContracts =
    [
          typeof(ICafeteriaPolicyProvider)
        , typeof(IEmployeeFactSource)
        , typeof(IEmployeeIdScanPublisher)
        , typeof(IWorkforceReferenceSource)
    ];

    internal static void Register(
        WebApplicationBuilder builder)
    {
        IConfigurationSection section =
            builder.Configuration.GetSection("IntegrationPlugin");

        if (!section.GetValue("Enabled", true))
            throw new InvalidOperationException(
                "Aegis.Cafeteria.Services requires an integration plugin.");

        string configuredPath = section["AssemblyPath"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException(
                "IntegrationPlugin:AssemblyPath is required.");

        string assemblyPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(
                Path.Combine(builder.Environment.ContentRootPath, configuredPath));

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException(
                "Configured Cafeteria integration plugin assembly was not found.",
                assemblyPath);

        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
            assemblyPath);

        Dictionary<Type, Type> implementationByContract = [];

        foreach (Type contract in RequiredContracts)
        {
            Type[] matches = assembly
                .GetTypes()
                .Where(type =>
                       type.IsClass
                    && !type.IsAbstract
                    && type.IsPublic
                    && contract.IsAssignableFrom(type))
                .ToArray();

            if (matches.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Integration plugin '{assembly.GetName().Name}' does not implement required contract {contract.FullName}.");
            }

            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Integration plugin '{assembly.GetName().Name}' has multiple implementations of required contract {contract.FullName}: {string.Join(", ", matches.Select(type => type.FullName))}.");
            }

            implementationByContract[contract] = matches[0];
        }

        foreach (Type implementation in implementationByContract.Values.Distinct())
        {
            builder.Services.AddSingleton(
                implementation,
                serviceProvider => ActivatorUtilities.CreateInstance(
                    serviceProvider,
                    implementation));
        }

        foreach ((Type contract, Type implementation) in implementationByContract)
        {
            builder.Services.AddSingleton(
                contract,
                serviceProvider => serviceProvider.GetRequiredService(
                    implementation));
        }
    }
}
