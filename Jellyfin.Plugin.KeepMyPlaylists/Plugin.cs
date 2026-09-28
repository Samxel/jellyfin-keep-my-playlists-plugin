using System;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// Keeps playlist entries (and their position) when a movie or episode file is replaced.
/// </summary>
public class Plugin : BasePlugin<BasePluginConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="xmlSerializer">The xml serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => "Keep My Playlists";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("50558b42-cf75-4263-880f-7bd2e766907c");

    /// <inheritdoc />
    public override string Description => "Puts replaced movies and episodes back into their playlists, at the same position.";
}
