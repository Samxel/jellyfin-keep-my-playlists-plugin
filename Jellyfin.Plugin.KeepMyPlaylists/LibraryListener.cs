using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// Updates the snapshot when a playlist changes and restores entries when new or updated videos show up.
/// </summary>
public sealed class LibraryListener : IHostedService, IDisposable
{
    private static readonly TimeSpan _playlistDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _restoreDelay = TimeSpan.FromSeconds(30);

    private readonly ILibraryManager _libraryManager;
    private readonly PlaylistKeeper _keeper;
    private readonly ILogger<LibraryListener> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pending = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryListener"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="keeper">The playlist keeper.</param>
    /// <param name="logger">The logger.</param>
    public LibraryListener(ILibraryManager libraryManager, PlaylistKeeper keeper, ILogger<LibraryListener> logger)
    {
        _libraryManager = libraryManager;
        _keeper = keeper;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        _libraryManager.ItemRemoved += OnItemChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        _libraryManager.ItemRemoved -= OnItemChanged;
        await _stopping.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Dispose();
        foreach (var cts in _pending.Values)
        {
            cts.Dispose();
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs e)
    {
        switch (e.Item)
        {
            case Playlist playlist:
                Schedule(playlist.Id, _playlistDelay, ct => _keeper.SnapshotAsync(playlist.Id, ct));
                break;
            case Video:
                // New or re-identified videos may replace a missing playlist entry.
                Schedule(Guid.Empty, _restoreDelay, _keeper.RunAsync);
                break;
        }
    }

    private void Schedule(Guid key, TimeSpan delay, Func<CancellationToken, Task> action)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        if (_pending.TryGetValue(key, out var previous))
        {
            previous.Cancel();
        }

        _pending[key] = cts;
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    await action(_stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Keeping playlists failed");
                }
                finally
                {
                    _pending.TryRemove(new(key, cts));
                    cts.Dispose();
                }
            },
            CancellationToken.None);
    }
}
