# Jellyfin Keep My Playlists

Keeps your playlists intact when a movie or episode is **replaced**, e.g. when Radarr/Sonarr upgrades a file or you swap a file by hand.

Jellyfin drops an item from every playlist as soon as its file disappears. The new file becomes a new item, so without this plugin it is simply missing from the playlist.

## Features

- **Restores replaced items at the same position:** the new version is found by IMDb/TMDB/TVDB id. Episodes are matched by series and season/episode.
- **Keeps the order:** each entry goes back in front of the entry that used to follow it.
- **Respects your changes:**
  - Entries you remove from a playlist are never added back.
  - Items that stay gone for more than 24 hours count as deleted on purpose and are forgotten.
- **Automatic:** reacts to library changes within seconds, and also runs at startup and every 6 hours (*Scheduled Tasks → Keep playlists*).

Requires **Jellyfin 12.1**. Only replacements that happen after the plugin is installed can be restored.

## Installation

1. Jellyfin Dashboard → **Plugins** → **Repositories** → add:
   ```
   https://raw.githubusercontent.com/Samxel/jellyfin-keep-my-playlists-plugin/master/manifest.json
   ```
2. Install **Keep My Playlists** from the catalog and restart Jellyfin.
