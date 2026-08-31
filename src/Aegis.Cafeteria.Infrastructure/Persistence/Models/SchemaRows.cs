namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class LocalSchemaInfoRow
{
    public int Id { get; set; } = 1;
    public int SchemaVersion { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
