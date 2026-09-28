namespace Aegis.Cafeteria.Services.Identity;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Identity;
using Common.Security.Abstractions;
using Common.Security.Models;

public sealed class CommonSecurityEmployeeMetadataProvider
(
      IServiceProvider services
    , EmployeeMetadataOptions options
) : IEmployeeMetadataProvider
{
    public string SourceId => options.SourceId;

    public async Task<IReadOnlyDictionary<int, EmployeeMetadataPatch>> GetAsync
    (
          IReadOnlyList<ExternalCustomerSnapshot> employees
        , CancellationToken cancellationToken = default
    )
    {
        IDirectoryProfileProvider? directory = services.GetService<IDirectoryProfileProvider>();
        if (directory is null)
        {
            throw new InvalidOperationException("The authentication plugin has no directory provider. Set EmployeeMetadata:Mode to None or configure an independent metadata plugin.");
        }

        string[] names = employees.Select(employee => employee.LoginName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyDictionary<string, DirectoryUserProfile> profiles =
            await directory.GetByLoginNamesAsync(names, cancellationToken);
        DirectoryProfileCache cache = new();
        cache.Replace(profiles);
        Dictionary<int, EmployeeMetadataPatch> result = new();
        foreach (ExternalCustomerSnapshot employee in employees)
        {
            DirectoryUserProfile? profile = cache.Get(employee.LoginName);
            if (profile is null)
            {
                continue;
            }

            result[employee.CustomerId] = new()
            {
                  EmailAddress = new(profile.EmailAddress ?? string.Empty, this.SourceId)
                , Department = new(profile.Department ?? string.Empty, this.SourceId)
                // The legacy directory contract also uses blank on manager lookup failure.
                // Until it supplies field-level status, only nonblank manager answers are authoritative.
                , Manager = string.IsNullOrWhiteSpace(profile.Manager) ? null : new(profile.Manager, this.SourceId)
                , Extension = new(profile.Extension ?? string.Empty, this.SourceId)
            };
        }

        return result;
    }
}
