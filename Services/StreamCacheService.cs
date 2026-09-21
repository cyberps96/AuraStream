using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace sopfiy.Services;

public class StreamCacheService
{
    private record CachedStream(string DirectUrl, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, CachedStream> _cache = new();
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlightResolutions = new();
    private readonly SemaphoreSlim _preloadThrottler = new(2, 2);

    public bool TryGet(string videoId, out string directUrl)
    {
        if (_cache.TryGetValue(videoId, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            directUrl = cached.DirectUrl;
            return true;
        }

        directUrl = string.Empty;
        return false;
    }

    public void Set(string videoId, string directUrl, TimeSpan? ttl = null)
    {
        var expiry = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromHours(2));
        _cache[videoId] = new CachedStream(directUrl, expiry);
    }

    public async Task<string?> GetOrResolveStreamAsync(YoutubeClient youtube, string videoId, CancellationToken token = default)
    {
        if (TryGet(videoId, out var cachedUrl))
        {
            return cachedUrl;
        }

        return await _inFlightResolutions.GetOrAdd(videoId, async id =>
        {
            try
            {
                var manifest = await youtube.Videos.Streams.GetManifestAsync(id, token);
                var audioStreams = manifest.GetAudioOnlyStreams().ToList();

                var fastStream = audioStreams.Where(s => s.Container == Container.Mp4).GetWithHighestBitrate()
                                 ?? audioStreams.GetWithHighestBitrate();

                if (fastStream != null)
                {
                    Set(id, fastStream.Url);
                    return fastStream.Url;
                }
            }
            catch
            {
            }
            finally
            {
                _inFlightResolutions.TryRemove(id, out _);
            }

            return null;
        });
    }

    public void QueuePreload(YoutubeClient youtube, IEnumerable<string> videoIds)
    {
        _ = Task.Run(async () =>
        {
            foreach (var id in videoIds)
            {
                if (string.IsNullOrWhiteSpace(id) || _cache.ContainsKey(id))
                    continue;

                await _preloadThrottler.WaitAsync();
                try
                {
                    if (!_cache.ContainsKey(id))
                    {
                        var manifest = await youtube.Videos.Streams.GetManifestAsync(id);
                        var audioStreams = manifest.GetAudioOnlyStreams().ToList();

                        var audioStream = audioStreams.Where(s => s.Container == Container.Mp4).GetWithHighestBitrate()
                                         ?? audioStreams.GetWithHighestBitrate();

                        if (audioStream != null)
                        {
                            Set(id, audioStream.Url);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    _preloadThrottler.Release();
                }
            }
        });
    }
}