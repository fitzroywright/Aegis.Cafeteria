namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;

public sealed class IntegrationPluginManifest : ICafeteriaIntegrationPlugin
{
    public string Id => "ffp-manager";
    public string DisplayName => "FFP Manager";
    public Version Version => typeof(IntegrationPluginManifest).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
}
