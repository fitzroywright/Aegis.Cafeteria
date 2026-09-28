namespace Aegis.Cafeteria.Services.Identity;

using Aegis.Cafeteria.Application.Integration;

using System.Reflection;
using System.Runtime.Loader;

public static class EmployeeMetadataRegistration
{
    public static void Register(IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        EmployeeMetadataOptions options = configuration.GetSection("EmployeeMetadata").Get<EmployeeMetadataOptions>() ?? new();
        string[] validFields = ["EmailAddress", "Department", "Manager", "Extension"];
        if (options.Fields.Any(field => !validFields.Contains(field, StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("EmployeeMetadata:Fields contains an unknown field.");
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IEmployeeMetadataProvider)))
        {
            throw new InvalidOperationException("Configure exactly one employee metadata provider through EmployeeMetadata.");
        }

        services.AddSingleton(options);
        services.AddSingleton<EmployeeMetadataStore>();
        if (options.Mode.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (options.Mode.Equals("CommonSecurity", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(options.SourceId);
            services.AddSingleton<IEmployeeMetadataProvider, CommonSecurityEmployeeMetadataProvider>();
            return;
        }

        if (!options.Mode.Equals("Plugin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("EmployeeMetadata:Mode must be None, CommonSecurity, or Plugin.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.AssemblyPath);
        string path = Path.GetFullPath(options.AssemblyPath, contentRoot);
        AssemblyDependencyResolver resolver = new(path);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string? dependency = resolver.ResolveAssemblyToPath(name);
            return dependency is null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency);
        };
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        Type[] implementations = assembly.GetTypes().Where(type =>
            type.IsPublic && type.IsClass && !type.IsAbstract && typeof(IEmployeeMetadataProvider).IsAssignableFrom(type)).ToArray();
        if (implementations.Length != 1)
        {
            throw new InvalidOperationException("The employee metadata plugin must export exactly one IEmployeeMetadataProvider.");
        }

        services.AddSingleton<IEmployeeMetadataProvider>(provider =>
            (IEmployeeMetadataProvider)ActivatorUtilities.CreateInstance(provider, implementations[0]));
    }
}
