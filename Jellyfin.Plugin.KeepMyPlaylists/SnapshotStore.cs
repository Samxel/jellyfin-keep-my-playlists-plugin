using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KeepMyPlaylists;

/// <summary>
/// Persists the ordered entries of every playlist, so entries can be restored after their item was deleted.
/// </summary>
public class SnapshotStore
{
    private readonly string _file;
    private readonly ILogger<SnapshotStore> _logger;
    private readonly object _lock = new();
    private Dictionary<Guid, List<SnapshotEntry>>? _snapshots;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    public SnapshotStore(IApplicationPaths applicationPaths, ILogger<SnapshotStore> logger)
    {
        ArgumentNullException.ThrowIfNull(applicationPaths);
        _file = Path.Combine(applicationPaths.PluginConfigurationsPath, "KeepMyPlaylists", "snapshots.json");
        _logger = logger;
    }

    /// <summary>
    /// Runs an action on the snapshots while holding the lock and saves them afterwards.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">The action.</param>
    /// <returns>The result of the action.</returns>
    public T Update<T>(Func<Dictionary<Guid, List<SnapshotEntry>>, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_lock)
        {
            _snapshots ??= Load();
            var result = action(_snapshots);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_snapshots));
            File.Move(tmp, _file, true);
            return result;
        }
    }

    private Dictionary<Guid, List<SnapshotEntry>> Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                return JsonSerializer.Deserialize<Dictionary<Guid, List<SnapshotEntry>>>(File.ReadAllText(_file)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogError(ex, "Could not read {File}", _file);
        }

        return [];
    }
}
