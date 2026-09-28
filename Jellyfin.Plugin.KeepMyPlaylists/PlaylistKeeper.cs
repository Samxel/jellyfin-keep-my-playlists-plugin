using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// Remembers the entries of every playlist and puts a replaced movie or episode back at its old position.
/// Jellyfin removes a playlist entry as soon as its item is deleted (e.g. when Radarr replaces the file),
/// so the order has to be remembered by the plugin.
/// </summary>
public sealed class PlaylistKeeper : IDisposable
{
    private static readonly string[] _keys = ["Imdb", "Tmdb", "Tvdb"];
    // A replacement (Radarr upgrade, file swapped by hand) shows up within minutes. Anything missing for longer
    // was deleted on purpose and is forgotten, so it is not re-added if it is downloaded again later.
    private static readonly TimeSpan _restoreWindow = TimeSpan.FromHours(24);

    private readonly ILibraryManager _libraryManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly IUserManager _userManager;
    private readonly SnapshotStore _store;
    private readonly ILogger<PlaylistKeeper> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistKeeper"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="playlistManager">The playlist manager.</param>
    /// <param name="userManager">The user manager.</param>
    /// <param name="store">The snapshot store.</param>
    /// <param name="logger">The logger.</param>
    public PlaylistKeeper(ILibraryManager libraryManager, IPlaylistManager playlistManager, IUserManager userManager, SnapshotStore store, ILogger<PlaylistKeeper> logger)
    {
        _libraryManager = libraryManager;
        _playlistManager = playlistManager;
        _userManager = userManager;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    /// <summary>
    /// Updates the snapshots of all playlists and restores replaced entries.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var playlist in GetPlaylists())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RestoreAsync(playlist).ConfigureAwait(false);
                Snapshot(playlist.Id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Updates the snapshot of one playlist after it changed.
    /// </summary>
    /// <param name="playlistId">The playlist id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task.</returns>
    public async Task SnapshotAsync(Guid playlistId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Snapshot(playlistId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static SnapshotEntry ToEntry(BaseItem item)
    {
        var entry = new SnapshotEntry
        {
            ItemId = item.Id,
            Kind = item.GetBaseItemKind().ToString(),
            Name = item is Episode e ? $"{e.SeriesName} S{e.ParentIndexNumber:00}E{e.IndexNumber:00}" : item.Name
        };
        Copy(item.ProviderIds, entry.ProviderIds);
        if (item is Episode episode)
        {
            Copy(episode.Series?.ProviderIds, entry.SeriesProviderIds);
            entry.Season = episode.ParentIndexNumber;
            entry.Episode = episode.IndexNumber;
        }

        return entry;
    }

    private static void Copy(Dictionary<string, string>? from, Dictionary<string, string> to)
    {
        foreach (var key in _keys)
        {
            if (from is not null && from.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                to[key] = value;
            }
        }
    }

    private List<MediaBrowser.Controller.Playlists.Playlist> GetPlaylists()
        => _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.Playlist], Recursive = true })
            .OfType<MediaBrowser.Controller.Playlists.Playlist>()
            .ToList();

    /// <summary>
    /// Stores the current entries of the playlist. Entries whose item no longer exists stay in the snapshot at
    /// their relative position for a while (the item was replaced or deleted); entries the user removed from the
    /// playlist are forgotten right away, because their item still exists.
    /// </summary>
    private void Snapshot(Guid playlistId)
    {
        if (_libraryManager.GetItemById(playlistId) is not MediaBrowser.Controller.Playlists.Playlist playlist)
        {
            _store.Update(s => s.Remove(playlistId));
            return;
        }

        var current = playlist.GetManageableItems().Select(i => ToEntry(i.Item2)).ToList();
        _store.Update(snapshots =>
        {
            var previous = snapshots.GetValueOrDefault(playlistId) ?? [];
            var present = current.Select(e => e.ItemId).ToHashSet();
            var now = DateTime.UtcNow;
            for (var i = 0; i < previous.Count; i++)
            {
                var old = previous[i];
                if (present.Contains(old.ItemId) || _libraryManager.GetItemById(old.ItemId) is not null)
                {
                    continue;
                }

                old.MissingSinceUtc ??= now;
                if (now - old.MissingSinceUtc > _restoreWindow)
                {
                    continue;
                }

                // Keep the missing entry in front of the next entry that is still in the playlist.
                var next = previous.Skip(i + 1).FirstOrDefault(p => present.Contains(p.ItemId));
                var index = next is null ? -1 : current.FindIndex(c => c.ItemId == next.ItemId);
                current.Insert(index < 0 ? current.Count : index, old);
            }

            snapshots[playlistId] = current;
            return true;
        });
    }

    private async Task RestoreAsync(MediaBrowser.Controller.Playlists.Playlist playlist)
    {
        var entries = _store.Update(s => s.GetValueOrDefault(playlist.Id)?.ToList() ?? []);
        var user = _userManager.GetUserById(playlist.OwnerUserId);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (_libraryManager.GetItemById(entry.ItemId) is not null
                || (entry.MissingSinceUtc is { } since && DateTime.UtcNow - since > _restoreWindow))
            {
                continue;
            }

            var replacement = FindReplacement(entry, user);
            if (replacement is null)
            {
                continue;
            }

            var linked = playlist.LinkedChildren.Select(c => c.ItemId).ToList();
            if (!linked.Contains(replacement.Id))
            {
                // Insert in front of the next entry that is still in the playlist.
                int? position = null;
                foreach (var next in entries.Skip(i + 1))
                {
                    var index = linked.IndexOf(next.ItemId);
                    if (index >= 0)
                    {
                        position = index;
                        break;
                    }
                }

                await _playlistManager.AddItemToPlaylistAsync(playlist.Id, [replacement.Id], position, playlist.OwnerUserId).ConfigureAwait(false);
                playlist = _libraryManager.GetItemById(playlist.Id) as MediaBrowser.Controller.Playlists.Playlist ?? playlist;
                _logger.LogInformation("Restored {Name} in playlist {Playlist} at position {Position}", entry.Name, playlist.Name, (position ?? linked.Count) + 1);
            }

            var replaced = ToEntry(replacement);
            entries[i] = replaced;
            var oldId = entry.ItemId;
            _store.Update(s =>
            {
                var list = s.GetValueOrDefault(playlist.Id);
                var index = list?.FindIndex(e => e.ItemId == oldId) ?? -1;
                if (index >= 0)
                {
                    list![index] = replaced;
                }

                return true;
            });
        }
    }

    private BaseItem? FindReplacement(SnapshotEntry entry, Jellyfin.Database.Implementations.Entities.User? user)
    {
        if (!Enum.TryParse<BaseItemKind>(entry.Kind, out var kind))
        {
            return null;
        }

        if (entry.ProviderIds.Count > 0)
        {
            var match = Query(kind, entry.ProviderIds, user)
                .FirstOrDefault(i => kind != BaseItemKind.Episode
                    || (i.ParentIndexNumber == entry.Season && i.IndexNumber == entry.Episode));
            if (match is not null)
            {
                return match;
            }
        }

        // Episodes often have no ids of their own: find the series, then the same season and episode.
        if (kind == BaseItemKind.Episode && entry.SeriesProviderIds.Count > 0 && entry.Season.HasValue && entry.Episode.HasValue)
        {
            foreach (var series in Query(BaseItemKind.Series, entry.SeriesProviderIds, user))
            {
                var episodes = _libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = [BaseItemKind.Episode],
                    AncestorIds = [series.Id],
                    ParentIndexNumber = entry.Season,
                    IndexNumber = entry.Episode,
                    Recursive = true,
                    IsVirtualItem = false
                });
                var episode = episodes.Count > 0 ? episodes[0] : null;
                if (episode is not null)
                {
                    return episode;
                }
            }
        }

        return null;
    }

    private IReadOnlyList<BaseItem> Query(BaseItemKind kind, Dictionary<string, string> providerIds, Jellyfin.Database.Implementations.Entities.User? user)
        => _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [kind],
            HasAnyProviderId = new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase),
            Recursive = true,
            IsVirtualItem = false
        });
}
