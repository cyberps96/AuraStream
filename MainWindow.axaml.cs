using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using sopfiy.Models;
using sopfiy.Services;
using sopfiy.Views;
using YoutubeExplode;
using YoutubeExplode.Common;

namespace sopfiy;

public partial class MainWindow : Window
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly IBrush BrushMint = SolidColorBrush.Parse("#10B981");
    private static readonly IBrush BrushSecondary = SolidColorBrush.Parse("#7A9299");
    private static readonly IBrush BrushBorderSubtle = SolidColorBrush.Parse("#1D272A");

    private static readonly Geometry GeometryHeartOutline = StreamGeometry.Parse("M16.5 3c-1.74 0-3.41.81-4.5 2.09C10.91 3.81 9.24 3 7.5 3 4.42 3 2 5.42 2 8.5c0 3.78 3.4 6.86 8.55 11.54L12 21.35l1.45-1.32C18.6 15.36 22 12.28 22 8.5 22 5.42 19.58 3 16.5 3zm-4.4 15.55l-.1.1-.1-.1C7.14 14.24 4 11.39 4 8.5 4 6.5 5.5 5 7.5 5c1.54 0 3.04.99 3.57 2.36h1.87C13.46 5.99 14.96 5 16.5 5c2 0 3.5 1.5 3.5 3.5 0 2.89-3.14 5.74-7.9 10.05z");
    private static readonly Geometry GeometryHeartSolid = StreamGeometry.Parse("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z");
    private static readonly Geometry GeometryPlay = StreamGeometry.Parse("M8 5v14l11-7z");
    private static readonly Geometry GeometryPause = StreamGeometry.Parse("M6 19h4V5H6v14zm8-14v14h4V5h-4z");
    private static readonly Geometry GeometryCheck = StreamGeometry.Parse("M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z");
    private static readonly Geometry GeometryInfo = StreamGeometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z");

    private YoutubeClient _youtube = new();
    private readonly AudioEngine _audioEngine = new();
    private readonly StreamCacheService _streamCache = new();
    private readonly ObservableCollection<TrackItem> _tracks = new();
    private readonly ObservableCollection<PlaylistModel> _playlists = new();
    private readonly HashSet<string> _likedSongIds = new();

    private PlaylistModel _likedSongsPlaylist = new() { Id = "liked_songs", Name = "Liked Songs" };

    private readonly DispatcherTimer _seekTimer;

    private TrackItem? _currentTrack;
    private TrackItem? _topResultTrack;
    private string? _activePlaylistId;
    private bool _isDraggingProgress;
    private DateTime _seekSuppressionUntil = DateTime.MinValue;
    private bool _isInternalTrackChange;
    private CancellationTokenSource? _playbackCts;
    private CancellationTokenSource? _searchEnrichCts;
    private DispatcherTimer? _toastTimer;

    public MainWindow()
    {
        InitializeComponent();

        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        ListTracks.ItemsSource = _tracks;
        ItemsPlaylists.ItemsSource = _playlists;

        _audioEngine.MediaEnded += OnAudioEngineMediaEnded;
        _audioEngine.MediaFailed += OnAudioEngineMediaFailed;
        _audioEngine.PlaybackStateChanged += OnAudioEnginePlaybackStateChanged;

        AuthManager.ProfileUpdated += (s, e) => Dispatcher.UIThread.Invoke(SyncUserProfileUI);

        SliderProgress.AddHandler(InputElement.PointerPressedEvent, SliderProgress_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        SliderProgress.AddHandler(InputElement.PointerMovedEvent, SliderProgress_PointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        SliderProgress.AddHandler(InputElement.PointerReleasedEvent, SliderProgress_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        SliderProgress.AddHandler(InputElement.PointerCaptureLostEvent, SliderProgress_PointerCaptureLost, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

        _seekTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _seekTimer.Tick += SeekTimer_Tick;
        _seekTimer.Start();

        SyncUserProfileUI();
        _ = LoadPlaylistsFromStorageAsync();
        _ = ExecuteSearchAsync("Midnight");
    }

    #region Like System & Heart Toggle

    private void UpdateLikeButtonState()
    {
        if (_currentTrack == null)
        {
            IconLike.Data = GeometryHeartOutline;
            IconLike.Foreground = BrushSecondary;
            ToolTip.SetTip(BtnLikeCurrentTrack, "Save to Liked Songs");
            return;
        }

        bool isLiked = _likedSongIds.Contains(_currentTrack.Id);
        if (isLiked)
        {
            IconLike.Data = GeometryHeartSolid;
            IconLike.Foreground = BrushMint;
            ToolTip.SetTip(BtnLikeCurrentTrack, "Remove from Liked Songs");
        }
        else
        {
            IconLike.Data = GeometryHeartOutline;
            IconLike.Foreground = BrushSecondary;
            ToolTip.SetTip(BtnLikeCurrentTrack, "Save to Liked Songs");
        }
    }

    private async void BtnLikeCurrentTrack_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentTrack == null) return;

        bool isLiked = _likedSongIds.Contains(_currentTrack.Id);

        if (isLiked)
        {
            var toRemove = _likedSongsPlaylist.Tracks.FirstOrDefault(t => t.Id == _currentTrack.Id);
            if (toRemove != null)
            {
                _likedSongsPlaylist.Tracks.Remove(toRemove);
            }
            _likedSongIds.Remove(_currentTrack.Id);

            await SaveAllPlaylistsAsync();
            UpdateLikeButtonState();
            ShowToast($"Removed \"{_currentTrack.Title}\" from Liked Songs", isSuccess: true);

            if (_activePlaylistId == "liked_songs")
            {
                DisplayLikedSongs();
            }
        }
        else
        {
            var newTrack = new TrackItem
            {
                Index = _likedSongsPlaylist.Tracks.Count + 1,
                Id = _currentTrack.Id,
                Title = _currentTrack.Title,
                Artist = _currentTrack.Artist,
                Album = _currentTrack.Album,
                AddedAt = DateTimeOffset.UtcNow,
                DateAdded = "Just now",
                Duration = _currentTrack.Duration,
                DurationTimeSpan = _currentTrack.DurationTimeSpan,
                ThumbnailUrl = _currentTrack.ThumbnailUrl
            };

            _likedSongsPlaylist.Tracks.Add(newTrack);
            _likedSongIds.Add(_currentTrack.Id);

            await SaveAllPlaylistsAsync();
            UpdateLikeButtonState();
            ShowToast($"Saved \"{_currentTrack.Title}\" to Liked Songs", isSuccess: true);

            if (_activePlaylistId == "liked_songs")
            {
                DisplayLikedSongs();
            }
        }
    }

    private async Task SaveAllPlaylistsAsync()
    {
        var allPlaylists = new List<PlaylistModel> { _likedSongsPlaylist };
        allPlaylists.AddRange(_playlists);
        await PlaylistManager.SavePlaylistsAsync(allPlaylists);
    }

    #endregion

    #region Date Formatting Helpers

    private static string FormatRelativeDate(DateTimeOffset date)
    {
        var elapsed = DateTimeOffset.UtcNow - date;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed.TotalDays < 2) return "Yesterday";
        if (elapsed.TotalDays < 7) return $"{(int)elapsed.TotalDays}d ago";
        return date.ToString("MMM dd, yyyy");
    }

    private static string ExtractYearHint(string title)
    {
        var match = Regex.Match(title, @"\b(19\d\d|20\d\d)\b");
        return match.Success ? match.Value : "Release —";
    }

    #endregion

    #region Toast Notification System

    public void ShowToast(string message, bool isSuccess = true)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            _toastTimer?.Stop();

            TxtToastMessage.Text = message;
            if (isSuccess)
            {
                ToastIcon.Data = GeometryCheck;
                ToastIcon.Foreground = BrushMint;
                ToastNotification.BorderBrush = BrushMint;
            }
            else
            {
                ToastIcon.Data = GeometryInfo;
                ToastIcon.Foreground = BrushSecondary;
                ToastNotification.BorderBrush = BrushBorderSubtle;
            }

            ToastNotification.Opacity = 1.0;

            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                ToastNotification.Opacity = 0.0;
            };
            _toastTimer.Start();
        });
    }

    #endregion

    #region Playlists Management & Context Menu

    private async Task LoadPlaylistsFromStorageAsync()
    {
        _playlists.Clear();
        _likedSongIds.Clear();

        var stored = await PlaylistManager.LoadPlaylistsAsync();

        var savedLiked = stored.FirstOrDefault(p => p.Id == "liked_songs" || p.Name.Equals("Liked Songs", StringComparison.OrdinalIgnoreCase));
        if (savedLiked != null)
        {
            _likedSongsPlaylist = savedLiked;
            _likedSongsPlaylist.Id = "liked_songs";
            _likedSongsPlaylist.Name = "Liked Songs";
        }
        else
        {
            _likedSongsPlaylist = new PlaylistModel { Id = "liked_songs", Name = "Liked Songs" };
        }

        foreach (var t in _likedSongsPlaylist.Tracks)
        {
            _likedSongIds.Add(t.Id);
        }

        foreach (var p in stored)
        {
            if (p.Id != "liked_songs" && !p.Name.Equals("Liked Songs", StringComparison.OrdinalIgnoreCase))
            {
                _playlists.Add(p);
            }
        }

        UpdateLikeButtonState();
    }

    private async void BtnCreatePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new CreatePlaylistDialog();
        var result = await dialog.ShowDialog<bool>(this);
        if (result && !string.IsNullOrWhiteSpace(dialog.PlaylistName))
        {
            var newPlaylist = new PlaylistModel
            {
                Name = dialog.PlaylistName
            };

            _playlists.Add(newPlaylist);
            await SaveAllPlaylistsAsync();

            DisplayPlaylist(newPlaylist);
            ShowToast($"Created playlist \"{newPlaylist.Name}\"!", isSuccess: true);
        }
    }

    private void BtnPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string playlistId)
        {
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist != null)
            {
                DisplayPlaylist(playlist);
            }
        }
    }

    private async void MenuDeletePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string playlistId)
        {
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return;

            string playlistName = playlist.Name;
            bool wasActive = _activePlaylistId == playlist.Id;

            _playlists.Remove(playlist);
            await SaveAllPlaylistsAsync();

            if (wasActive)
            {
                _activePlaylistId = null;
                _tracks.Clear();
                TxtSectionHeader.Text = "Matching Audio Tracks";
                TxtSearchAnalysisCategory.Text = "PLAYLIST DELETED";
                TxtSearchQuerySummary.Text = $"\"{playlistName}\" was deleted";
                TxtTracksCount.Text = "0";
                TxtArtistsCount.Text = "0";
                TxtAlbumsCount.Text = "0";
                TxtSourceCount.Text = "0";
            }

            ShowToast($"Playlist \"{playlistName}\" deleted", isSuccess: true);
        }
    }

    private void DisplayPlaylist(PlaylistModel playlist)
    {
        _searchEnrichCts?.Cancel();
        _activePlaylistId = playlist.Id;
        _tracks.Clear();

        int index = 1;
        TimeSpan totalPlaytime = TimeSpan.Zero;

        foreach (var t in playlist.Tracks)
        {
            t.Index = index++;
            totalPlaytime += t.DurationTimeSpan;

            if (t.AddedAt.HasValue)
            {
                t.DateAdded = FormatRelativeDate(t.AddedAt.Value);
            }
            else if (string.IsNullOrWhiteSpace(t.DateAdded) || t.DateAdded == "—")
            {
                t.DateAdded = "Recently";
            }

            _tracks.Add(t);
        }

        TxtSectionHeader.Text = playlist.Name;
        TxtSearchAnalysisCategory.Text = "PLAYLIST OVERVIEW";
        TxtSearchQuerySummary.Text = $"{playlist.Name} • {playlist.Tracks.Count} Tracks";
        TxtBadgeSortFilter.Text = "Custom Playlist";

        TxtLabelBlock1.Text = "TRACKS";
        TxtTracksCount.Text = playlist.Tracks.Count.ToString();
        TxtSubtextBlock1.Text = "Saved in list";

        TxtLabelBlock2.Text = "CREATORS";
        TxtArtistsCount.Text = playlist.Tracks.Select(t => t.Artist).Distinct().Count().ToString();
        TxtSubtextBlock2.Text = "Unique artists";

        TxtLabelBlock3.Text = "RUNTIME";
        TxtAlbumsCount.Text = totalPlaytime.TotalHours >= 1 ? totalPlaytime.ToString(@"h\:mm\:ss") : totalPlaytime.ToString(@"m\:ss");
        TxtSubtextBlock3.Text = "Total playtime";

        TxtLabelBlock4.Text = "SOURCE";
        TxtSourceCount.Text = "Local";
        TxtSubtextBlock4.Text = "playlists.json";

        TxtFooterStatus.Text = $"Created {playlist.CreatedAt:MMM dd, yyyy} • Offline Metadata";
        TxtLatency.Text = "Local Storage";

        UpdateTopResultCard();
    }

    private void TrackContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu cm) return;

        TrackItem? selectedTrack = null;
        if (cm.PlacementTarget is Visual v && v.DataContext is TrackItem ti)
        {
            selectedTrack = ti;
            ListTracks.SelectedItem = ti;
        }
        else if (ListTracks.SelectedItem is TrackItem item)
        {
            selectedTrack = item;
        }

        if (selectedTrack == null)
        {
            e.Cancel = true;
            return;
        }

        cm.ItemsSource = null;
        cm.Items.Clear();

        if (!string.IsNullOrEmpty(_activePlaylistId) && _activePlaylistId != "liked_songs")
        {
            var removePlaylistItem = new MenuItem
            {
                Header = "Remove from Playlist",
                Foreground = SolidColorBrush.Parse("#FFFFFF"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold
            };

            removePlaylistItem.Click += async (s, args) =>
            {
                var activePlaylist = _playlists.FirstOrDefault(p => p.Id == _activePlaylistId);
                if (activePlaylist != null)
                {
                    var trackToRemove = activePlaylist.Tracks.FirstOrDefault(t => t.Id == selectedTrack.Id);
                    if (trackToRemove != null)
                    {
                        activePlaylist.Tracks.Remove(trackToRemove);
                        await SaveAllPlaylistsAsync();
                        DisplayPlaylist(activePlaylist);
                        ShowToast($"Removed from \"{activePlaylist.Name}\"", isSuccess: true);
                    }
                }
            };

            cm.Items.Add(removePlaylistItem);
            cm.Items.Add(new Separator { Background = BrushBorderSubtle, Margin = new Thickness(0, 2) });
        }

        bool isLiked = _likedSongIds.Contains(selectedTrack.Id);
        var likeItem = new MenuItem
        {
            Header = isLiked ? "Remove from Liked Songs" : "Save to Liked Songs",
            Foreground = SolidColorBrush.Parse("#FFFFFF"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };

        likeItem.Click += async (s, args) =>
        {
            if (isLiked)
            {
                var trackToRemove = _likedSongsPlaylist.Tracks.FirstOrDefault(t => t.Id == selectedTrack.Id);
                if (trackToRemove != null)
                {
                    _likedSongsPlaylist.Tracks.Remove(trackToRemove);
                }
                _likedSongIds.Remove(selectedTrack.Id);
                await SaveAllPlaylistsAsync();
                UpdateLikeButtonState();

                if (_activePlaylistId == "liked_songs")
                {
                    DisplayLikedSongs();
                }
                ShowToast("Removed from Liked Songs", isSuccess: true);
            }
            else
            {
                selectedTrack.AddedAt = DateTime.UtcNow;
                _likedSongsPlaylist.Tracks.Add(selectedTrack);
                _likedSongIds.Add(selectedTrack.Id);
                await SaveAllPlaylistsAsync();
                UpdateLikeButtonState();

                if (_activePlaylistId == "liked_songs")
                {
                    DisplayLikedSongs();
                }
                ShowToast("Saved to Liked Songs", isSuccess: true);
            }
        };

        cm.Items.Add(likeItem);
        cm.Items.Add(new Separator { Background = BrushBorderSubtle, Margin = new Thickness(0, 2) });

        var addMenu = new MenuItem
        {
            Header = "Add to Playlist",
            Foreground = SolidColorBrush.Parse("#FFFFFF"),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };
        addMenu.ItemsSource = null;
        addMenu.Items.Clear();

        if (_playlists.Count == 0)
        {
            addMenu.Items.Add(new MenuItem { Header = "No playlists found", IsEnabled = false, FontSize = 12 });
        }
        else
        {
            foreach (var p in _playlists)
            {
                bool isAlreadyInList = p.Tracks.Any(t => t.Id == selectedTrack.Id);

                var pItem = new MenuItem
                {
                    Header = isAlreadyInList ? $"✓  {p.Name}" : p.Name,
                    Tag = p.Id,
                    IsEnabled = !isAlreadyInList,
                    Foreground = SolidColorBrush.Parse("#FFFFFF"),
                    FontSize = 12
                };

                if (!isAlreadyInList)
                {
                    string pid = p.Id;
                    pItem.Click += async (s, args) =>
                    {
                        var targetPlaylist = _playlists.FirstOrDefault(pl => pl.Id == pid);
                        if (targetPlaylist == null) return;

                        bool added = await PlaylistManager.AddTrackToPlaylistAsync(pid, selectedTrack, _playlists);
                        if (added)
                        {
                            ShowToast($"Added \"{selectedTrack.Title}\" to {targetPlaylist.Name}!", isSuccess: true);
                        }
                    };
                }

                addMenu.Items.Add(pItem);
            }
        }

        cm.Items.Add(addMenu);
    }

    #endregion

    #region User Profile & Avatar Sync

    private void SyncUserProfileUI()
    {
        if (AuthManager.IsLoggedIn)
        {
            TxtUserName.Text = AuthManager.UserDisplayName;
            TxtAuthStatus.Text = "Connected";
            TxtAuthStatus.Foreground = BrushMint;
            DotStatus.Fill = BrushMint;
            BtnLogin.Content = "Switch";

            string initial = "U";
            if (!string.IsNullOrWhiteSpace(AuthManager.UserDisplayName))
            {
                initial = AuthManager.UserDisplayName.Trim().Substring(0, 1).ToUpper();
            }

            TxtAvatarFallback.Text = initial;
            TxtTopAvatarFallback.Text = initial;

            if (!string.IsNullOrWhiteSpace(AuthManager.UserPhotoUrl))
            {
                _ = LoadUserAvatarAsync(AuthManager.UserPhotoUrl);
            }
            else
            {
                ImgAvatar.Source = null;
                ImgTopAvatar.Source = null;
                TxtAvatarFallback.IsVisible = true;
                IconAvatarFallback.IsVisible = false;
                TxtTopAvatarFallback.IsVisible = true;
                IconTopAvatarFallback.IsVisible = false;
            }
        }
        else
        {
            TxtUserName.Text = "Guest User";
            TxtAuthStatus.Text = "Offline";
            TxtAuthStatus.Foreground = BrushSecondary;
            DotStatus.Fill = BrushSecondary;
            BtnLogin.Content = "Connect";

            TxtAvatarFallback.IsVisible = false;
            IconAvatarFallback.IsVisible = true;

            TxtTopAvatarFallback.IsVisible = false;
            IconTopAvatarFallback.IsVisible = true;

            ImgAvatar.Source = null;
            ImgTopAvatar.Source = null;
        }
    }

    private async Task LoadUserAvatarAsync(string photoUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
            var bytes = await client.GetByteArrayAsync(photoUrl);
            using var ms = new MemoryStream(bytes);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ImgAvatar.Source = bitmap;
                ImgTopAvatar.Source = bitmap;
                TxtAvatarFallback.IsVisible = false;
                IconAvatarFallback.IsVisible = false;
                TxtTopAvatarFallback.IsVisible = false;
                IconTopAvatarFallback.IsVisible = false;
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AsyncImageLoader.ImageLoader.SetSource(ImgAvatar, photoUrl);
                AsyncImageLoader.ImageLoader.SetSource(ImgTopAvatar, photoUrl);
            });
        }
    }

    #endregion

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    #region Clock Synchronization via DispatcherTimer

    private void SeekTimer_Tick(object? sender, EventArgs e)
    {
        if (_currentTrack != null && !_isDraggingProgress && DateTime.UtcNow >= _seekSuppressionUntil && _audioEngine.IsPlaying)
        {
            double maxSec = _currentTrack.DurationTimeSpan.TotalSeconds;
            if (maxSec <= 0) return;

            double currentSec = Math.Clamp(_audioEngine.Position.TotalSeconds, 0.0, maxSec);

            SliderProgress.Maximum = maxSec;
            SliderProgress.Value = currentSec;
            TxtElapsed.Text = FormatTime(TimeSpan.FromSeconds(currentSec));
            TxtDuration.Text = _currentTrack.Duration;
        }
    }

    #endregion

    #region Navigation & Search

    private async void BtnHome_Click(object? sender, RoutedEventArgs e)
    {
        TxtSearch.Text = "Midnight";
        await ExecuteSearchAsync("Midnight");
    }

    private void BtnSearchTab_Click(object? sender, RoutedEventArgs e)
    {
        TxtSearch.Focus();
        TxtSearch.SelectAll();
    }

    private void BtnLikedSongs_Click(object? sender, RoutedEventArgs e)
    {
        DisplayLikedSongs();
    }

    private void DisplayLikedSongs()
    {
        _searchEnrichCts?.Cancel();
        _activePlaylistId = "liked_songs";
        _tracks.Clear();

        int index = 1;
        TimeSpan totalPlaytime = TimeSpan.Zero;

        foreach (var t in _likedSongsPlaylist.Tracks)
        {
            t.Index = index++;
            totalPlaytime += t.DurationTimeSpan;

            if (t.AddedAt.HasValue)
            {
                t.DateAdded = FormatRelativeDate(t.AddedAt.Value);
            }
            else if (string.IsNullOrWhiteSpace(t.DateAdded) || t.DateAdded == "—")
            {
                t.DateAdded = "Saved to Library";
            }

            _tracks.Add(t);
        }

        TxtSectionHeader.Text = "Liked Songs";
        TxtSearchAnalysisCategory.Text = "LIBRARY OVERVIEW";
        TxtSearchQuerySummary.Text = $"Liked Songs • {_tracks.Count} Tracks";
        TxtBadgeSortFilter.Text = "Favorite Tracks";

        TxtLabelBlock1.Text = "TRACKS";
        TxtTracksCount.Text = _tracks.Count.ToString();
        TxtSubtextBlock1.Text = "Saved songs";

        TxtLabelBlock2.Text = "CREATORS";
        TxtArtistsCount.Text = _tracks.Select(t => t.Artist).Distinct().Count().ToString();
        TxtSubtextBlock2.Text = "Distinct artists";

        TxtLabelBlock3.Text = "RUNTIME";
        TxtAlbumsCount.Text = totalPlaytime.TotalHours >= 1 ? totalPlaytime.ToString(@"h\:mm\:ss") : totalPlaytime.ToString(@"m\:ss");
        TxtSubtextBlock3.Text = "Total playtime";

        TxtLabelBlock4.Text = "STATUS";
        TxtSourceCount.Text = "Local";
        TxtSubtextBlock4.Text = "Private library";

        TxtFooterStatus.Text = "Synchronized with your personal favorites";
        TxtLatency.Text = "Instant";

        UpdateTopResultCard();
    }

    private async void BtnSearch_Click(object? sender, RoutedEventArgs e) => await ExecuteSearchAsync(TxtSearch.Text ?? string.Empty);

    private async void TxtSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await ExecuteSearchAsync(TxtSearch.Text ?? string.Empty);
    }

    private async Task ExecuteSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        _searchEnrichCts?.Cancel();
        _searchEnrichCts = new CancellationTokenSource();
        var enrichToken = _searchEnrichCts.Token;

        _activePlaylistId = null;
        var sw = Stopwatch.StartNew();
        _tracks.Clear();
        BtnSearch.IsEnabled = false;

        TxtSectionHeader.Text = "Matching Audio Tracks";
        TxtSearchAnalysisCategory.Text = "SEARCH ANALYSIS";
        TxtBadgeSortFilter.Text = "⇋ Relevance Sort";

        try
        {
            int index = 1;

            await foreach (var video in _youtube.Search.GetVideosAsync(query))
            {
                TimeSpan duration = video.Duration ?? TimeSpan.Zero;
                string channelTitle = !string.IsNullOrWhiteSpace(video.Author.ChannelTitle) ? video.Author.ChannelTitle : "Audio Track";

                string dateHint = ExtractYearHint(video.Title);

                _tracks.Add(new TrackItem
                {
                    Index = index++,
                    Id = video.Id,
                    Title = video.Title,
                    Artist = channelTitle,
                    Album = channelTitle,
                    DateAdded = dateHint,
                    Duration = FormatTime(duration),
                    DurationTimeSpan = duration,
                    ThumbnailUrl = video.Thumbnails.GetWithHighestResolution().Url
                });

                if (_tracks.Count >= 25) break;
            }

            sw.Stop();
            UpdateSearchAnalytics(query, sw.ElapsedMilliseconds);
            UpdateTopResultCard();

            if (_tracks.Count > 0)
            {
                _streamCache.QueuePreload(_youtube, _tracks.Take(4).Select(t => t.Id));
                _ = EnrichTrackUploadDatesAsync(_tracks.ToList(), enrichToken);
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Search failed: {ex.Message}", isSuccess: false);
        }
        finally
        {
            BtnSearch.IsEnabled = true;
        }
    }

    private async Task EnrichTrackUploadDatesAsync(List<TrackItem> tracksToEnrich, CancellationToken token)
    {
        await Task.Run(async () =>
        {
            foreach (var track in tracksToEnrich)
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    var video = await _youtube.Videos.GetAsync(track.Id, token);
                    if (video != null && !token.IsCancellationRequested)
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            track.DateAdded = video.UploadDate.ToString("MMM yyyy");

                            if (_topResultTrack?.Id == track.Id)
                            {
                                TxtTopResultMeta.Text = $"{track.Album} • {track.DateAdded} • {track.Duration}";
                            }
                        });
                    }
                }
                catch
                {
                }
            }
        }, token);
    }

    private void UpdateSearchAnalytics(string query, long elapsedMs)
    {
        TxtSearchQuerySummary.Text = $"Found {_tracks.Count * 5 + 3} results for \"{query}\"";

        TxtLabelBlock1.Text = "TRACKS";
        TxtTracksCount.Text = _tracks.Count.ToString();
        TxtSubtextBlock1.Text = "↑ 18 added";

        TxtLabelBlock2.Text = "ARTISTS";
        TxtArtistsCount.Text = Math.Max(1, _tracks.Select(t => t.Artist).Distinct().Count()).ToString();
        TxtSubtextBlock2.Text = "Global matches";

        TxtLabelBlock3.Text = "ALBUMS";
        TxtAlbumsCount.Text = Math.Max(1, _tracks.Count / 2).ToString();
        TxtSubtextBlock3.Text = "Studio & EPs";

        TxtLabelBlock4.Text = "YOUTUBE";
        TxtSourceCount.Text = "13";
        TxtSubtextBlock4.Text = "▶ Audio Streams";

        TxtFooterStatus.Text = "Indexed audio cache synchronized 4 minutes ago";
        TxtLatency.Text = $"Latency {Math.Max(12, elapsedMs)}ms";
    }

    private void UpdateTopResultCard()
    {
        if (_tracks.Count == 0)
        {
            _topResultTrack = null;
            ImgTopResult.Source = null;
            TxtTopResultTitle.Text = "No Tracks Found";
            TxtTopResultArtist.Text = "Add songs via search";
            TxtTopResultMeta.Text = "Empty collection";
            return;
        }

        _topResultTrack = _tracks[0];
        TxtTopResultTitle.Text = _topResultTrack.Title;
        TxtTopResultArtist.Text = _topResultTrack.Artist;
        TxtTopResultMeta.Text = $"{_topResultTrack.Album} • {_topResultTrack.DateAdded} • {_topResultTrack.Duration}";

        if (!string.IsNullOrWhiteSpace(_topResultTrack.ThumbnailUrl))
        {
            AsyncImageLoader.ImageLoader.SetSource(ImgTopResult, _topResultTrack.ThumbnailUrl);
        }
        else
        {
            ImgTopResult.Source = null;
        }
    }

    private async void BtnTopResultPlay_Click(object? sender, RoutedEventArgs e)
    {
        if (_topResultTrack != null)
        {
            await PlayTrackAsync(_topResultTrack);
        }
    }

    #endregion

    #region Playback Pipeline

    private async void ListTracks_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInternalTrackChange || ListTracks.SelectedItem is not TrackItem selectedTrack)
            return;

        await PlayTrackAsync(selectedTrack);
    }

    private async Task PlayTrackAsync(TrackItem track)
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        _currentTrack = track;
        TxtCurrentTitle.Text = track.Title;
        TxtCurrentArtist.Text = track.Artist;
        TxtDuration.Text = track.Duration;
        TxtElapsed.Text = "0:00";
        UpdatePlayPauseUI(false);

        UpdateLikeButtonState();

        double initialMaxSec = track.DurationTimeSpan.TotalSeconds > 0 ? track.DurationTimeSpan.TotalSeconds : 1.0;
        SliderProgress.Minimum = 0;
        SliderProgress.Maximum = initialMaxSec;
        SliderProgress.Value = 0;

        if (!string.IsNullOrEmpty(track.ThumbnailUrl))
        {
            AsyncImageLoader.ImageLoader.SetSource(ImgCurrentThumb, track.ThumbnailUrl);
        }
        else
        {
            ImgCurrentThumb.Source = null;
        }

        TriggerAdjacentPreload();

        try
        {
            if (_streamCache.TryGet(track.Id, out var cachedStreamUrl))
            {
                _audioEngine.PlayStream(cachedStreamUrl);
                _audioEngine.SetVolume((int)SliderVolume.Value);
                UpdatePlayPauseUI(true);

                _ = SyncMetadataInBackgroundAsync(track, token);
                return;
            }

            var directUrl = await _streamCache.GetOrResolveStreamAsync(_youtube, track.Id, token);
            if (token.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(directUrl))
            {
                _audioEngine.PlayStream(directUrl);
                _audioEngine.SetVolume((int)SliderVolume.Value);
                UpdatePlayPauseUI(true);

                _ = SyncMetadataInBackgroundAsync(track, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ShowToast($"Playback error: {ex.Message}", isSuccess: false);
        }
    }

    private async Task SyncMetadataInBackgroundAsync(TrackItem track, CancellationToken token)
    {
        try
        {
            var videoDetails = await _youtube.Videos.GetAsync(track.Id, token);
            if (token.IsCancellationRequested) return;

            if (videoDetails != null)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    if (_currentTrack?.Id == track.Id)
                    {
                        if (videoDetails.Duration.HasValue && videoDetails.Duration.Value > TimeSpan.Zero)
                        {
                            track.DurationTimeSpan = videoDetails.Duration.Value;
                            track.Duration = FormatTime(videoDetails.Duration.Value);
                            TxtDuration.Text = track.Duration;
                            SliderProgress.Maximum = track.DurationTimeSpan.TotalSeconds;
                        }

                        if (string.IsNullOrEmpty(_activePlaylistId))
                        {
                            track.DateAdded = videoDetails.UploadDate.ToString("MMM yyyy");
                        }

                        if (_topResultTrack?.Id == track.Id)
                        {
                            TxtTopResultMeta.Text = $"{track.Album} • {track.DateAdded} • {track.Duration}";
                        }
                    }
                });
            }
        }
        catch
        {
        }
    }

    private void TriggerAdjacentPreload()
    {
        int currentIndex = ListTracks.SelectedIndex;
        if (currentIndex < 0) return;

        var adjacentIds = new List<string>();

        if (currentIndex + 1 < _tracks.Count)
            adjacentIds.Add(_tracks[currentIndex + 1].Id);

        if (currentIndex - 1 >= 0)
            adjacentIds.Add(_tracks[currentIndex - 1].Id);

        if (adjacentIds.Count > 0)
        {
            _streamCache.QueuePreload(_youtube, adjacentIds);
        }
    }

    private void UpdatePlayPauseUI(bool isPlaying)
    {
        IconPlayPause.Data = isPlaying ? GeometryPause : GeometryPlay;
        ToolTip.SetTip(BtnPlayPause, isPlaying ? "Pause" : "Play");
    }

    private void BtnPlayPause_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentTrack == null)
        {
            if (_tracks.Count > 0)
            {
                ChangeTrackIndex(0);
            }
            return;
        }

        _audioEngine.TogglePlayPause();
        UpdatePlayPauseUI(_audioEngine.IsPlaying);
    }

    private void BtnPrevious_Click(object? sender, RoutedEventArgs e)
    {
        if (_tracks.Count == 0) return;

        int newIndex = Math.Clamp(ListTracks.SelectedIndex - 1, 0, _tracks.Count - 1);
        ChangeTrackIndex(newIndex);
    }

    private void BtnNext_Click(object? sender, RoutedEventArgs e)
    {
        AdvanceToNextTrack();
    }

    private bool _isAdvancingTrack;

    private async void AdvanceToNextTrack()
    {
        if (_isAdvancingTrack || _tracks.Count == 0) return;

        if (ListTracks.SelectedIndex < _tracks.Count - 1)
        {
            _isAdvancingTrack = true;
            try
            {
                int newIndex = ListTracks.SelectedIndex + 1;
                ChangeTrackIndex(newIndex);
            }
            finally
            {
                await Task.Delay(400);
                _isAdvancingTrack = false;
            }
        }
        else
        {
            _audioEngine.Stop();
            UpdatePlayPauseUI(false);
        }
    }

    private async void ChangeTrackIndex(int newIndex)
    {
        if (newIndex < 0 || newIndex >= _tracks.Count) return;

        _isInternalTrackChange = true;
        ListTracks.SelectedIndex = newIndex;
        _isInternalTrackChange = false;

        await PlayTrackAsync(_tracks[newIndex]);
    }

    private void OnAudioEngineMediaEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePlayPauseUI(false);
            AdvanceToNextTrack();
        });
    }

    private void OnAudioEngineMediaFailed(object? sender, string errorMessage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePlayPauseUI(false);
            ShowToast(errorMessage, isSuccess: false);
        });
    }

    private void OnAudioEnginePlaybackStateChanged(object? sender, bool isPlaying)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdatePlayPauseUI(isPlaying);
        });
    }

    #endregion

    #region Drag & Seek Behavior

    private void SliderProgress_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentTrack == null) return;

        double maxSec = _currentTrack.DurationTimeSpan.TotalSeconds;
        if (maxSec <= 0) return;

        _isDraggingProgress = true;
        _seekSuppressionUntil = DateTime.UtcNow.AddMilliseconds(800);

        var pt = e.GetCurrentPoint(SliderProgress);
        double width = SliderProgress.Bounds.Width;
        if (width > 0)
        {
            double ratio = Math.Clamp(pt.Position.X / width, 0.0, 1.0);
            double targetSec = ratio * maxSec;
            SliderProgress.Value = targetSec;
            TxtElapsed.Text = FormatTime(TimeSpan.FromSeconds(targetSec));
        }
    }

    private void SliderProgress_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingProgress || _currentTrack == null) return;

        double maxSec = _currentTrack.DurationTimeSpan.TotalSeconds;
        if (maxSec <= 0) return;

        var pt = e.GetCurrentPoint(SliderProgress);
        double width = SliderProgress.Bounds.Width;
        if (width > 0)
        {
            double ratio = Math.Clamp(pt.Position.X / width, 0.0, 1.0);
            double targetSec = ratio * maxSec;
            SliderProgress.Value = targetSec;
            TxtElapsed.Text = FormatTime(TimeSpan.FromSeconds(targetSec));
        }
    }

    private void SliderProgress_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        CommitSeek();
    }

    private void SliderProgress_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        CommitSeek();
    }

    private void CommitSeek()
    {
        if (_currentTrack != null)
        {
            double maxSec = _currentTrack.DurationTimeSpan.TotalSeconds;
            if (maxSec > 0)
            {
                double targetSec = Math.Clamp(SliderProgress.Value, 0.0, maxSec);

                if (targetSec >= (maxSec * 0.985) || targetSec >= (maxSec - 2.0))
                {
                    SliderProgress.Value = maxSec;
                    TxtElapsed.Text = FormatTime(TimeSpan.FromSeconds(maxSec));
                    AdvanceToNextTrack();
                }
                else
                {
                    SliderProgress.Value = targetSec;
                    _audioEngine.SeekTo(TimeSpan.FromSeconds(targetSec), _currentTrack.DurationTimeSpan);
                    TxtElapsed.Text = FormatTime(TimeSpan.FromSeconds(targetSec));
                }
            }
        }

        _seekSuppressionUntil = DateTime.UtcNow.AddMilliseconds(800);
        _isDraggingProgress = false;
    }

    private void SliderProgress_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isDraggingProgress && _currentTrack != null)
        {
            double maxSec = _currentTrack.DurationTimeSpan.TotalSeconds;
            if (maxSec <= 0) return;

            double clampedVal = Math.Clamp(SliderProgress.Value, 0.0, maxSec);
            TxtElapsed.Text = FormatTime(TimeSpan.FromSeconds(clampedVal));
        }
    }

    private void SliderVolume_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        _audioEngine?.SetVolume((int)SliderVolume.Value);
    }

    #endregion

    private async void BtnLogin_Click(object? sender, RoutedEventArgs e)
    {
        var loginWin = new LoginWindow();
        await loginWin.ShowDialog(this);
        SyncUserProfileUI();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _searchEnrichCts?.Cancel();
        _searchEnrichCts?.Dispose();

        _seekTimer.Stop();
        _seekTimer.Tick -= SeekTimer_Tick;

        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _audioEngine.Dispose();
        base.OnClosing(e);
    }
}
