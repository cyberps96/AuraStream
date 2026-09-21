using System;
using System.Collections.Generic;

namespace sopfiy.Models;

public class PlaylistModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New Playlist";
    public List<TrackItem> Tracks { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}