namespace Aegis.Cafeteria.Application.Integration;

/// <summary>Optional employee facts, independent of operator authentication.</summary>
public interface IEmployeeMetadataProvider
{
    string SourceId { get; }

    Task<IReadOnlyDictionary<int, EmployeeMetadataPatch>> GetAsync
    (
          IReadOnlyList<ExternalCustomerSnapshot> employees
        , CancellationToken cancellationToken = default
    );
}

/// <summary>A null field means no answer. A supplied empty Value is an authoritative clear.</summary>
public sealed record EmployeeMetadataValue(string Value, string Source);

public sealed record EmployeeMetadataPatch
{
    public EmployeeMetadataValue? EmailAddress { get; init; }
    public EmployeeMetadataValue? Department { get; init; }
    public EmployeeMetadataValue? Manager { get; init; }
    public EmployeeMetadataValue? Extension { get; init; }

    public EmployeeMetadataPatch Merge(EmployeeMetadataPatch incoming) => new()
    {
          EmailAddress = incoming.EmailAddress ?? this.EmailAddress
        , Department = incoming.Department ?? this.Department
        , Manager = incoming.Manager ?? this.Manager
        , Extension = incoming.Extension ?? this.Extension
    };

    public static string Apply(EmployeeMetadataValue? update, string current)
    {
        return update is null ? current : update.Value?.Trim() ?? string.Empty;
    }

    public static EmployeeMetadataPatch FromLegacy(ExternalCustomerSnapshot employee) => new()
    {
          EmailAddress = LegacyValue(employee.EmailAddress)
        , Department = LegacyValue(employee.Department)
        , Manager = LegacyValue(employee.Manager)
        , Extension = LegacyValue(employee.Extension)
    };

    private static EmployeeMetadataValue? LegacyValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : new(value.Trim(), "workforce");
    }
}
