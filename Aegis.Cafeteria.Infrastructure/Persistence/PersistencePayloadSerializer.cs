namespace Aegis.Cafeteria.Infrastructure.Persistence;
using System.Text.Json;
public static class PersistencePayloadSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name}.");
}
