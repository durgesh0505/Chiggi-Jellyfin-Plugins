using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ChiggiStats.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ChiggiStats;

/// <summary>
/// The Chiggi Stats plugin main entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Chiggi Stats";

    /// <inheritdoc />
    public override Guid Id => new Guid("a8e82834-4b5e-4b16-a7c4-6ea5d3a4e312");

    /// <inheritdoc />
    public override string Description => "Detailed playback statistics and reporting for Jellyfin with advanced filtering by user, date, and media type.";

    /// <summary>Gets the current plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "chiggistats",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.stats.html",
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "bar_chart",
                DisplayName = "Chiggi Stats"
            },
            new PluginPageInfo
            {
                Name = "chiggistatsconfig",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.config.html"
            }
        };
    }
}
