namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;

public sealed class IntegrationPluginManifest : ICafeteriaIntegrationPlugin
{
    public string Id => "generic-integration-plane";
    public string DisplayName => "Generic Integration Plane";
    public Version Version => typeof(IntegrationPluginManifest).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
}
