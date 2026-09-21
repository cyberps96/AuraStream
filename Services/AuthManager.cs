using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YoutubeExplode;

namespace sopfiy.Services;

public class UserProfile
{
    public string DisplayName { get; set; } = "Guest User";
    public string PhotoUrl { get; set; } = string.Empty;
    public Dictionary<string, string> Cookies { get; set; } = new();
}

public static class AuthManager
{
    private static readonly string StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "sopfiy"
    );

    private static readonly string SessionPath = Path.Combine(StorageDirectory, "auth_session.json");

    private static UserProfile _currentProfile = new();

    public static bool IsLoggedIn => File.Exists(SessionPath) &&
                                     _currentProfile.Cookies != null &&
                                     _currentProfile.Cookies.Count > 0 &&
                                     _currentProfile.DisplayName != "Guest User";

    public static string UserDisplayName => _currentProfile.DisplayName;
    public static string UserPhotoUrl => _currentProfile.PhotoUrl;

    public static event EventHandler? ProfileUpdated;

    static AuthManager()
    {
        _ = LoadProfileAsync();
    }

    public static async Task LoadProfileAsync()
    {
        if (!File.Exists(SessionPath))
        {
            SetGuestState();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(SessionPath);
            var profile = JsonSerializer.Deserialize<UserProfile>(json);
            if (profile != null && profile.Cookies != null && profile.Cookies.Count > 0)
            {
                _currentProfile = profile;
                ProfileUpdated?.Invoke(null, EventArgs.Empty);

                if (string.IsNullOrWhiteSpace(_currentProfile.PhotoUrl) ||
                    _currentProfile.DisplayName.Equals("Connected User", StringComparison.OrdinalIgnoreCase) ||
                    _currentProfile.DisplayName.Equals("Google User", StringComparison.OrdinalIgnoreCase) ||
                    _currentProfile.DisplayName.Equals("Google Account", StringComparison.OrdinalIgnoreCase))
                {
                    _ = RefreshProfileFromSessionAsync();
                }

                return;
            }
        }
        catch
        {
        }

        SetGuestState();
    }

    public static void SignOut()
    {
        if (File.Exists(SessionPath))
        {
            try
            {
                File.Delete(SessionPath);
            }
            catch
            {
            }
        }
        SetGuestState();
    }

    public static Dictionary<string, string> ParseCookieString(string cookieHeader)
    {
        var dict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(cookieHeader)) return dict;

        var parts = cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var idx = part.IndexOf('=');
            if (idx > 0)
            {
                var key = part.Substring(0, idx).Trim();
                var val = part.Substring(idx + 1).Trim();
                dict[key] = val;
            }
        }
        return dict;
    }

    private static void SetGuestState()
    {
        _currentProfile = new UserProfile
        {
            DisplayName = "Guest User",
            PhotoUrl = string.Empty,
            Cookies = new Dictionary<string, string>()
        };
        ProfileUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static async Task SaveSessionAsync(Dictionary<string, string> cookies, string displayName, string photoUrl)
    {
        if (!Directory.Exists(StorageDirectory))
            Directory.CreateDirectory(StorageDirectory);

        string cleanPhoto = photoUrl?.Trim() ?? string.Empty;
        if (cleanPhoto.StartsWith("//"))
        {
            cleanPhoto = "https:" + cleanPhoto;
        }
        if (string.IsNullOrWhiteSpace(cleanPhoto) && !string.IsNullOrWhiteSpace(_currentProfile.PhotoUrl))
        {
            cleanPhoto = _currentProfile.PhotoUrl;
        }

        string cleanName = displayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanName) ||
            cleanName.Equals("Connected User", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("Google Account", StringComparison.OrdinalIgnoreCase) ||
            cleanName.Equals("Google User", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(_currentProfile.DisplayName) &&
                !_currentProfile.DisplayName.Equals("Guest User", StringComparison.OrdinalIgnoreCase) &&
                !_currentProfile.DisplayName.Equals("Connected User", StringComparison.OrdinalIgnoreCase) &&
                !_currentProfile.DisplayName.Equals("Google User", StringComparison.OrdinalIgnoreCase) &&
                !_currentProfile.DisplayName.Equals("Google Account", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = _currentProfile.DisplayName;
            }
            else
            {
                cleanName = "Google Account";
            }
        }

        _currentProfile = new UserProfile
        {
            DisplayName = cleanName,
            PhotoUrl = cleanPhoto,
            Cookies = cookies
        };

        var json = JsonSerializer.Serialize(_currentProfile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SessionPath, json);

        ProfileUpdated?.Invoke(null, EventArgs.Empty);
    }

    public static async Task<IReadOnlyList<Cookie>?> LoadCookiesAsync()
    {
        if (!File.Exists(SessionPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(SessionPath);
            var profile = JsonSerializer.Deserialize<UserProfile>(json);
            if (profile?.Cookies == null || profile.Cookies.Count == 0) return null;

            return profile.Cookies.Select(kvp => new Cookie(kvp.Key, kvp.Value, "/", ".youtube.com")).ToList();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<YoutubeClient> GetAuthenticatedClientAsync()
    {
        var cookies = await LoadCookiesAsync();
        if (cookies != null && cookies.Count > 0)
        {
            return new YoutubeClient(cookies);
        }

        return new YoutubeClient();
    }

    public static async Task RefreshProfileFromSessionAsync()
    {
        if (_currentProfile.Cookies == null || _currentProfile.Cookies.Count == 0)
            return;

        string? extractedName = null;
        string? extractedPhoto = null;

        var googleProfile = await FetchGoogleAccountProfileAsync();
        if (!string.IsNullOrWhiteSpace(googleProfile.Name))
            extractedName = googleProfile.Name;
        if (!string.IsNullOrWhiteSpace(googleProfile.PhotoUrl))
            extractedPhoto = googleProfile.PhotoUrl;

        if (string.IsNullOrWhiteSpace(extractedName) || string.IsNullOrWhiteSpace(extractedPhoto))
        {
            var ytProfile = await FetchInnerTubeProfileAsync();
            if (string.IsNullOrWhiteSpace(extractedName) && !string.IsNullOrWhiteSpace(ytProfile.Name))
                extractedName = ytProfile.Name;
            if (string.IsNullOrWhiteSpace(extractedPhoto) && !string.IsNullOrWhiteSpace(ytProfile.PhotoUrl))
                extractedPhoto = ytProfile.PhotoUrl;
        }

        if (!string.IsNullOrWhiteSpace(extractedName) || !string.IsNullOrWhiteSpace(extractedPhoto))
        {
            string finalName = !string.IsNullOrWhiteSpace(extractedName) ? extractedName : _currentProfile.DisplayName;
            string finalPhoto = !string.IsNullOrWhiteSpace(extractedPhoto) ? extractedPhoto : _currentProfile.PhotoUrl;

            await SaveSessionAsync(_currentProfile.Cookies, finalName, finalPhoto);
        }
    }

    private static async Task<(string? Name, string? PhotoUrl)> FetchGoogleAccountProfileAsync()
    {
        if (_currentProfile.Cookies == null || _currentProfile.Cookies.Count == 0)
            return (null, null);

        try
        {
            var container = new CookieContainer();
            foreach (var kvp in _currentProfile.Cookies)
            {
                try
                {
                    container.Add(new Cookie(kvp.Key, kvp.Value, "/", ".google.com"));
                    container.Add(new Cookie(kvp.Key, kvp.Value, "/", ".youtube.com"));
                }
                catch { }
            }

            var handler = new HttpClientHandler
            {
                CookieContainer = container,
                AllowAutoRedirect = true
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");

            var response = await client.GetAsync("https://myaccount.google.com/profile/name");
            if (!response.IsSuccessStatusCode)
                return (null, null);

            var html = await response.Content.ReadAsStringAsync();
            var match = Regex.Match(html, @"AF_initDataCallback\(\{key:\s*'ds:0'.*?data:(.*?), sideChannel:", RegexOptions.Singleline);
            if (!match.Success)
                return (null, null);

            string dataJson = match.Groups[1].Value.Trim();
            using var dataDoc = JsonDocument.Parse(dataJson);
            var root = dataDoc.RootElement;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 3)
                {
                    string? name = null;
                    var nameArray = first[2];
                    if (nameArray.ValueKind == JsonValueKind.Array && nameArray.GetArrayLength() > 1)
                    {
                        name = nameArray[1].GetString();
                    }

                    string? photo = null;
                    var photoArray = first[3];
                    if (photoArray.ValueKind == JsonValueKind.Array && photoArray.GetArrayLength() > 1)
                    {
                        photo = photoArray[1].GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(photo))
                    {
                        if (photo.Contains("="))
                        {
                            var basePhoto = photo.Substring(0, photo.IndexOf('='));
                            photo = $"{basePhoto}=s160-c-mo";
                        }
                        else
                        {
                            photo = $"{photo}=s160-c-mo";
                        }
                    }

                    return (name, photo);
                }
            }
        }
        catch
        {
        }

        return (null, null);
    }

    private static async Task<(string? Name, string? PhotoUrl)> FetchInnerTubeProfileAsync()
    {
        if (_currentProfile.Cookies == null || !_currentProfile.Cookies.TryGetValue("SAPISID", out var sapisid))
            return (null, null);

        try
        {
            var handler = new HttpClientHandler { UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string origin = "https://music.youtube.com";
            string hashInput = $"{timestamp} {sapisid} {origin}";
            string sha1Hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();

            var cookieHeader = string.Join("; ", _currentProfile.Cookies.Select(c => $"{c.Key}={c.Value}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://music.youtube.com/youtubei/v1/account/account_menu?prettyPrint=false");
            request.Headers.Add("Cookie", cookieHeader);
            request.Headers.Add("Authorization", $"SAPISIDHASH {timestamp}_{sha1Hash}");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("X-Origin", origin);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var payload = new
            {
                context = new
                {
                    client = new
                    {
                        clientName = "WEB_REMIX",
                        clientVersion = "1.20240101.01.00"
                    }
                }
            };

            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return (null, null);

            string responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            string? extractedName = null;
            string? extractedPhoto = null;

            if (root.TryGetProperty("actions", out var actions) && actions.GetArrayLength() > 0)
            {
                var popup = actions[0].GetProperty("openPopupAction").GetProperty("popup");
                if (popup.TryGetProperty("multiPageMenuRenderer", out var menu) &&
                    menu.TryGetProperty("header", out var header) &&
                    header.TryGetProperty("activeAccountHeaderRenderer", out var accountHeader))
                {
                    if (accountHeader.TryGetProperty("accountName", out var accNameEl))
                    {
                        if (accNameEl.TryGetProperty("simpleText", out var st))
                            extractedName = st.GetString();
                        else if (accNameEl.TryGetProperty("runs", out var runs) && runs.GetArrayLength() > 0)
                            extractedName = runs[0].GetProperty("text").GetString();
                    }

                    if (accountHeader.TryGetProperty("accountPhoto", out var photoEl) &&
                        photoEl.TryGetProperty("thumbnails", out var thumbs) &&
                        thumbs.GetArrayLength() > 0)
                    {
                        extractedPhoto = thumbs[thumbs.GetArrayLength() - 1].GetProperty("url").GetString();
                    }
                }
            }

            return (extractedName, extractedPhoto);
        }
        catch
        {
            return (null, null);
        }
    }
}