namespace Aegis.Cafeteria.Application.Integration;

public interface ICafeteriaIntegrationPlugin
{
    string Id { get; }
    string DisplayName { get; }
    Version Version { get; }
}
