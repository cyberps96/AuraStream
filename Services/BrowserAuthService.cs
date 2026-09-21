using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace sopfiy.Services;

public static class BrowserAuthService
{
    private static readonly string[] CandidateBrowsers =
    {
        "brave-browser",
        "google-chrome-stable",
        "google-chrome",
        "chromium",
        "chromium-browser",
        "microsoft-edge-stable",
        "microsoft-edge"
    };

    public static string? FindBrowserExecutable()
    {
        var envBrowser = Environment.GetEnvironmentVariable("CHROME_BIN");
        if (!string.IsNullOrWhiteSpace(envBrowser) && File.Exists(envBrowser))
            return envBrowser;

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathDirs = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var name in CandidateBrowsers)
        {
            foreach (var dir in pathDirs)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        string[] standardDirs = { "/usr/bin", "/usr/local/bin", "/var/lib/flatpak/exports/bin", "/snap/bin" };
        foreach (var name in CandidateBrowsers)
        {
            foreach (var dir in standardDirs)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<bool> StartInteractiveLoginAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var browserPath = FindBrowserExecutable();
        if (string.IsNullOrWhiteSpace(browserPath))
        {
            throw new InvalidOperationException("No compatible Chromium browser (Chrome, Brave, Chromium) was found on your system.");
        }

        int port = GetAvailablePort();
        string tempUserDataDir = Path.Combine(Path.GetTempPath(), "sopfiy-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempUserDataDir);

        const string loginUrl = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fmusic.youtube.com%2F";

        var psi = new ProcessStartInfo
        {
            FileName = browserPath,
            Arguments = $"--app=\"{loginUrl}\" " +
                        $"--remote-debugging-port={port} " +
                        $"--user-data-dir=\"{tempUserDataDir}\" " +
                        "--no-first-run " +
                        "--no-default-browser-check " +
                        "--window-size=540,780",
            UseShellExecute = false
        };

        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start browser process.");
            }

            progress?.Report("Google login window opened. Please sign in...");

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            string? wsUrl = null;

            var waitStopwatch = Stopwatch.StartNew();
            while (waitStopwatch.Elapsed < TimeSpan.FromSeconds(15) && !cancellationToken.IsCancellationRequested)
            {
                if (process.HasExited)
                    return false;

                try
                {
                    var versionJson = await httpClient.GetStringAsync($"http://127.0.0.1:{port}/json/version", cancellationToken);
                    using var doc = JsonDocument.Parse(versionJson);
                    if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsProp))
                    {
                        wsUrl = wsProp.GetString();
                        if (!string.IsNullOrWhiteSpace(wsUrl))
                            break;
                    }
                }
                catch
                {
                }

                await Task.Delay(250, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(wsUrl))
            {
                throw new TimeoutException("Could not connect to browser DevTools interface.");
            }

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

            int msgId = 1;
            var receiveBuffer = new byte[65536];

            while (!cancellationToken.IsCancellationRequested && !process.HasExited)
            {
                var sendBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = msgId++, method = "Storage.getCookies" }));
                await ws.SendAsync(sendBuffer, WebSocketMessageType.Text, true, cancellationToken);

                var sb = new StringBuilder();
                WebSocketReceiveResult recvResult;
                do
                {
                    recvResult = await ws.ReceiveAsync(receiveBuffer, cancellationToken);
                    sb.Append(Encoding.UTF8.GetString(receiveBuffer, 0, recvResult.Count));
                } while (!recvResult.EndOfMessage);

                string response = sb.ToString();

                var cookies = ExtractAuthCookies(response);
                if (cookies.ContainsKey("SAPISID") || cookies.ContainsKey("__Secure-3PAPISID"))
                {
                    progress?.Report("Session captured! Synchronizing Google account profile...");

                    await Task.Delay(1500, cancellationToken);
                    try
                    {
                        var secondBuffer = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id = msgId++, method = "Storage.getCookies" }));
                        await ws.SendAsync(secondBuffer, WebSocketMessageType.Text, true, cancellationToken);
                        var sb2 = new StringBuilder();
                        WebSocketReceiveResult recvResult2;
                        do
                        {
                            recvResult2 = await ws.ReceiveAsync(receiveBuffer, cancellationToken);
                            sb2.Append(Encoding.UTF8.GetString(receiveBuffer, 0, recvResult2.Count));
                        } while (!recvResult2.EndOfMessage);

                        var refreshedCookies = ExtractAuthCookies(sb2.ToString());
                        foreach (var kvp in refreshedCookies)
                        {
                            cookies[kvp.Key] = kvp.Value;
                        }
                    }
                    catch
                    {
                    }

                    await AuthManager.SaveSessionAsync(cookies, "Google Account", string.Empty);
                    await AuthManager.RefreshProfileFromSessionAsync();

                    progress?.Report($"Welcome, {AuthManager.UserDisplayName}!");
                    await Task.Delay(600, cancellationToken);
                    return true;
                }

                await Task.Delay(1000, cancellationToken);
            }

            return false;
        }
        finally
        {
            if (process != null && !process.HasExited)
            {
                try { process.Kill(true); } catch { }
            }

            try
            {
                if (Directory.Exists(tempUserDataDir))
                {
                    Directory.Delete(tempUserDataDir, true);
                }
            }
            catch
            {
            }
        }
    }

    private static Dictionary<string, string> ExtractAuthCookies(string cdpJsonResponse)
    {
        var result = new Dictionary<string, string>();
        try
        {
            using var doc = JsonDocument.Parse(cdpJsonResponse);
            if (doc.RootElement.TryGetProperty("result", out var resProp) &&
                resProp.TryGetProperty("cookies", out var cookiesProp) &&
                cookiesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cookiesProp.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString();
                    var value = item.GetProperty("value").GetString();
                    var domain = item.TryGetProperty("domain", out var dProp) ? dProp.GetString() : string.Empty;

                    if (!string.IsNullOrEmpty(name) && value != null)
                    {
                        if (domain != null && (domain.Contains("youtube.com") || domain.Contains("google.com")))
                        {
                            result[name] = value;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return result;
    }
}
