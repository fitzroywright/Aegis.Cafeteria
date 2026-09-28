namespace Aegis.Cafeteria.Services.Identity;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;

/// <summary>Durable last-known authoritative fields. Missing answers never erase facts.</summary>
public sealed class EmployeeMetadataStore
(
      IDbContextFactory<ServiceDbContext> factory
    , EmployeeMetadataOptions options
    , IEnumerable<IEmployeeMetadataProvider> providers
)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public EmployeeMetadataPatch SelectCurrent(EmployeeMetadataPatch patch)
    {
        string? source = providers.SingleOrDefault()?.SourceId;
        EmployeeMetadataValue? Select(EmployeeMetadataValue? value, string field)
        {
            string? owner = options.Owns(field) ? source : "workforce";
            return value is not null && owner is not null && value.Source == owner
                ? value with { Value = value.Value?.Trim() ?? string.Empty }
                : null;
        }

        return new()
        {
              EmailAddress = Select(patch.EmailAddress, nameof(patch.EmailAddress))
            , Department = Select(patch.Department, nameof(patch.Department))
            , Manager = Select(patch.Manager, nameof(patch.Manager))
            , Extension = Select(patch.Extension, nameof(patch.Extension))
        };
    }

    public async Task<EmployeeMetadataPatch> GetAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        EmployeeMetadataRow? row = await database.EmployeeMetadata.FindAsync([customerId], cancellationToken);
        return SelectCurrent(Decode(row));
    }

    public async Task ApplyAsync(IReadOnlyDictionary<int, EmployeeMetadataPatch> updates, CancellationToken cancellationToken = default)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
            foreach (KeyValuePair<int, EmployeeMetadataPatch> update in updates)
            {
                EmployeeMetadataRow? row = await database.EmployeeMetadata.FindAsync([update.Key], cancellationToken);
                EmployeeMetadataPatch merged = Decode(row).Merge(SelectCurrent(update.Value));
                if (row is null)
                {
                    row = new() { CustomerId = update.Key };
                    database.EmployeeMetadata.Add(row);
                }

                row.PayloadJson = JsonSerializer.Serialize(merged);
            }

            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            this.gate.Release();
        }
    }

    public async Task<ExternalCustomerSnapshot[]> EnrichAsync(IReadOnlyList<ExternalCustomerSnapshot> customers, CancellationToken cancellationToken)
    {
        EmployeeMetadataValue? Workforce(EmployeeMetadataValue? value)
        {
            return value is null ? null : value with { Source = "workforce" };
        }

        await ApplyAsync(customers.ToDictionary(employee => employee.CustomerId, employee =>
        {
            EmployeeMetadataPatch patch = employee.Metadata ?? EmployeeMetadataPatch.FromLegacy(employee);
            return patch with
            {
                  EmailAddress = Workforce(patch.EmailAddress)
                , Department = Workforce(patch.Department)
                , Manager = Workforce(patch.Manager)
                , Extension = Workforce(patch.Extension)
            };
        }), cancellationToken);
        await using ServiceDbContext database = await factory.CreateDbContextAsync(cancellationToken);
        int[] ids = customers.Select(employee => employee.CustomerId).ToArray();
        Dictionary<int, EmployeeMetadataRow> rows = await database.EmployeeMetadata
            .Where(row => ids.Contains(row.CustomerId)).ToDictionaryAsync(row => row.CustomerId, cancellationToken);
        return customers.Select(employee =>
        {
            EmployeeMetadataPatch patch = SelectCurrent(Decode(rows.GetValueOrDefault(employee.CustomerId)));
            return employee with
            {
                  Metadata = patch
                , EmailAddress = EmployeeMetadataPatch.Apply(patch.EmailAddress, string.Empty)
                , Department = EmployeeMetadataPatch.Apply(patch.Department, string.Empty)
                , Manager = EmployeeMetadataPatch.Apply(patch.Manager, string.Empty)
                , Extension = EmployeeMetadataPatch.Apply(patch.Extension, string.Empty)
            };
        }).ToArray();
    }

    private static EmployeeMetadataPatch Decode(EmployeeMetadataRow? row)
    {
        return row is null ? new() : JsonSerializer.Deserialize<EmployeeMetadataPatch>(row.PayloadJson) ?? new();
    }
}
