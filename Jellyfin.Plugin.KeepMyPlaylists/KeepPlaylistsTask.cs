using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// Snapshots all playlists and restores replaced entries (at startup and every 6 hours).
/// </summary>
public class KeepPlaylistsTask : IScheduledTask
{
    private readonly PlaylistKeeper _keeper;

    /// <summary>
    /// Initializes a new instance of the <see cref="KeepPlaylistsTask"/> class.
    /// </summary>
    /// <param name="keeper">The playlist keeper.</param>
    public KeepPlaylistsTask(PlaylistKeeper keeper) => _keeper = keeper;

    /// <inheritdoc />
    public string Name => "Keep playlists";

    /// <inheritdoc />
    public string Key => "KeepMyPlaylists";

    /// <inheritdoc />
    public string Description => "Remembers all playlist entries and puts replaced movies and episodes back at their position.";

    /// <inheritdoc />
    public string Category => "Keep My Playlists";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        await _keeper.RunAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger };
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks };
    }
}
