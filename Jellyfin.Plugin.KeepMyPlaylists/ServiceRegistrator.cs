using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// Registers the plugin services.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SnapshotStore>();
        serviceCollection.AddSingleton<PlaylistKeeper>();
        serviceCollection.AddHostedService<LibraryListener>();
    }
}
