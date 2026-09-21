using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using sopfiy.Models;

namespace sopfiy.Services;

public static class PlaylistManager
{
    private static readonly string StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "sopfiy"
    );

    private static readonly string PlaylistsFilePath = Path.Combine(StorageDirectory, "playlists.json");

    public static async Task<List<PlaylistModel>> LoadPlaylistsAsync()
    {
        if (!File.Exists(PlaylistsFilePath))
        {
            var initialList = new List<PlaylistModel>
            {
                new() { Name = "My Favorites" },
                new() { Name = "Chillout Lounge" }
            };
            await SavePlaylistsAsync(initialList);
            return initialList;
        }

        try
        {
            var json = await File.ReadAllTextAsync(PlaylistsFilePath);
            var playlists = JsonSerializer.Deserialize<List<PlaylistModel>>(json);
            return playlists ?? new List<PlaylistModel>();
        }
        catch
        {
            return new List<PlaylistModel>();
        }
    }

    public static async Task SavePlaylistsAsync(IEnumerable<PlaylistModel> playlists)
    {
        if (!Directory.Exists(StorageDirectory))
            Directory.CreateDirectory(StorageDirectory);

        var json = JsonSerializer.Serialize(playlists, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(PlaylistsFilePath, json);
    }

    public static async Task<bool> AddTrackToPlaylistAsync(string playlistId, TrackItem track, IEnumerable<PlaylistModel> currentPlaylists)
    {
        var playlist = currentPlaylists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist == null) return false;

        if (!playlist.Tracks.Any(t => t.Id == track.Id))
        {
            var utcNow = DateTimeOffset.UtcNow;

            var newTrack = new TrackItem
            {
                Index = playlist.Tracks.Count + 1,
                Id = track.Id,
                Title = track.Title,
                Artist = track.Artist,
                Album = track.Album,
                AddedAt = utcNow,
                DateAdded = "Just now",
                Duration = track.Duration,
                DurationTimeSpan = track.DurationTimeSpan,
                ThumbnailUrl = track.ThumbnailUrl
            };

            playlist.Tracks.Add(newTrack);
            await SavePlaylistsAsync(currentPlaylists);
            return true;
        }

        return false;
    }
}