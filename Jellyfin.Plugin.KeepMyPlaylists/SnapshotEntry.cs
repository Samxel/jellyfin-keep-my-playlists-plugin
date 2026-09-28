using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// One playlist entry as remembered by the plugin.
/// </summary>
public class SnapshotEntry
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the item kind (Movie, Episode, …).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name, for the log.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the provider ids of the item (Imdb, Tmdb, Tvdb, …).
    /// </summary>
#pragma warning disable CA2227 // Setter needed for JSON deserialization
    public Dictionary<string, string> ProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the provider ids of the series, for episodes.
    /// </summary>
    public Dictionary<string, string> SeriesProviderIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore CA2227

    /// <summary>
    /// Gets or sets the season number, for episodes.
    /// </summary>
    public int? Season { get; set; }

    /// <summary>
    /// Gets or sets the episode number, for episodes.
    /// </summary>
    public int? Episode { get; set; }

    /// <summary>
    /// Gets or sets when the item disappeared from the library, or <c>null</c> while it exists.
    /// </summary>
    public DateTime? MissingSinceUtc { get; set; }
}
