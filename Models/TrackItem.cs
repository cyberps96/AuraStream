using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace sopfiy.Models;

public class TrackItem : INotifyPropertyChanged
{
    private int _index;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = "Single";
    private string _dateAdded = "—";
    private DateTimeOffset? _addedAt;
    private string _duration = "--:--";
    private TimeSpan _durationTimeSpan;
    private string _thumbnailUrl = string.Empty;
    private bool _isPlayingNow;

    public int Index
    {
        get => _index;
        set => SetField(ref _index, value);
    }

    public required string Id { get; set; }

    public required string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public required string Artist
    {
        get => _artist;
        set => SetField(ref _artist, value);
    }

    public string Album
    {
        get => _album;
        set => SetField(ref _album, value);
    }

    public string DateAdded
    {
        get => _dateAdded;
        set => SetField(ref _dateAdded, value);
    }

    public DateTimeOffset? AddedAt
    {
        get => _addedAt;
        set => SetField(ref _addedAt, value);
    }

    public required string Duration
    {
        get => _duration;
        set => SetField(ref _duration, value);
    }

    public TimeSpan DurationTimeSpan
    {
        get => _durationTimeSpan;
        set => SetField(ref _durationTimeSpan, value);
    }

    public required string ThumbnailUrl
    {
        get => _thumbnailUrl;
        set => SetField(ref _thumbnailUrl, value);
    }

    public bool IsPlayingNow
    {
        get => _isPlayingNow;
        set => SetField(ref _isPlayingNow, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}