using Jellyfin.Plugin.ChiggiStats.Data;
using Jellyfin.Plugin.ChiggiStats.Tracking;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.ChiggiStats;

/// <summary>
/// Registers Chiggi Stats services with Jellyfin's DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SqliteRepository>();
        serviceCollection.AddSingleton<InventoryReportService>();
        serviceCollection.AddHostedService<PlaybackTracker>();
    }
}
