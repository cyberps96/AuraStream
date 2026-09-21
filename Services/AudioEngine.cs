using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using LibVLCSharp.Shared;

namespace sopfiy.Services;

public class AudioEngine : IDisposable
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private bool _isPlaying;
    private bool _hasMedia;
    private bool _isDisposed;

    public event EventHandler? MediaEnded;
    public event EventHandler<string>? MediaFailed;
    public event EventHandler<bool>? PlaybackStateChanged;

    static AudioEngine()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            NativeLibrary.SetDllImportResolver(typeof(LibVLC).Assembly, (libraryName, assembly, searchPath) =>
            {
                if (libraryName == "libvlc")
                {
                    if (NativeLibrary.TryLoad("/lib64/libvlc.so.5", out var handle)) return handle;
                    if (NativeLibrary.TryLoad("libvlc.so.5", out handle)) return handle;
                    if (NativeLibrary.TryLoad("libvlc.so", out handle)) return handle;
                }
                if (libraryName == "libvlccore")
                {
                    if (NativeLibrary.TryLoad("/lib64/libvlccore.so.9", out var handle)) return handle;
                    if (NativeLibrary.TryLoad("libvlccore.so.9", out handle)) return handle;
                    if (NativeLibrary.TryLoad("libvlccore.so", out handle)) return handle;
                }
                return IntPtr.Zero;
            });
        }
    }

    public AudioEngine()
    {
        Core.Initialize();
        _libVLC = new LibVLC("--no-video", "--network-caching=1000");
        _mediaPlayer = new MediaPlayer(_libVLC);

        _mediaPlayer.Playing += (s, e) =>
        {
            SetPlaybackState(true);
        };

        _mediaPlayer.Paused += (s, e) =>
        {
            SetPlaybackState(false);
        };

        _mediaPlayer.Stopped += (s, e) =>
        {
            SetPlaybackState(false);
        };

        _mediaPlayer.EndReached += (s, e) =>
        {
            SetPlaybackState(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                MediaEnded?.Invoke(this, EventArgs.Empty);
            });
        };

        _mediaPlayer.EncounteredError += (s, e) =>
        {
            SetPlaybackState(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                MediaFailed?.Invoke(this, "Playback error encountered");
            });
        };
    }

    private void SetPlaybackState(bool isPlaying)
    {
        if (_isPlaying == isPlaying) return;
        _isPlaying = isPlaying;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            PlaybackStateChanged?.Invoke(this, isPlaying);
        });
    }

    public TimeSpan Position
    {
        get
        {
            if (_isDisposed) return TimeSpan.Zero;
            try
            {
                long ms = _mediaPlayer.Time;
                return ms > 0 ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }
    }

    public bool IsPlaying => !_isDisposed && _isPlaying;

    public void PlayStream(string directUrl)
    {
        if (string.IsNullOrWhiteSpace(directUrl) || _isDisposed) return;

        try
        {
            using var media = new Media(_libVLC, new Uri(directUrl));
            _hasMedia = true;
            SetPlaybackState(true);
            _mediaPlayer.Play(media);
        }
        catch (Exception ex)
        {
            SetPlaybackState(false);
            MediaFailed?.Invoke(this, $"Failed to play media: {ex.Message}");
        }
    }

    public void Play()
    {
        if (_isDisposed || !_hasMedia) return;
        try
        {
            SetPlaybackState(true);
            _mediaPlayer.Play();
        }
        catch { }
    }

    public void Pause()
    {
        if (_isDisposed || !_hasMedia) return;
        try
        {
            SetPlaybackState(false);
            _mediaPlayer.Pause();
        }
        catch { }
    }

    public void Stop()
    {
        SetPlaybackState(false);
        if (!_isDisposed)
        {
            try
            {
                _mediaPlayer.Stop();
            }
            catch { }
        }
    }

    public void TogglePlayPause()
    {
        if (_isDisposed || !_hasMedia) return;
        try
        {
            if (_isPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }
        catch { }
    }

    public void SetVolume(int volume)
    {
        if (_isDisposed) return;
        try
        {
            _mediaPlayer.Volume = Math.Clamp(volume, 0, 100);
        }
        catch { }
    }

    public void SeekTo(float normalizedPosition)
    {
        if (_isDisposed) return;
        try
        {
            _mediaPlayer.Position = Math.Clamp(normalizedPosition, 0f, 0.99f);
        }
        catch { }
    }

    public void SeekTo(TimeSpan position)
    {
        SeekTo(position, TimeSpan.Zero);
    }

    public void SeekTo(TimeSpan position, TimeSpan totalDuration)
    {
        if (_isDisposed) return;
        try
        {
            if (totalDuration > TimeSpan.Zero)
            {
                float ratio = (float)(position.TotalSeconds / totalDuration.TotalSeconds);
                SeekTo(ratio);
            }
            else
            {
                _mediaPlayer.Time = (long)position.TotalMilliseconds;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _hasMedia = false;
        _isPlaying = false;
        try
        {
            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }
        catch { }
        GC.SuppressFinalize(this);
    }
}