using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Astra.Core.Control;
using Astra.Core.Index;
using Astra.Core.Settings;
using Microsoft.Win32;

namespace Astra.Core.Browser;

/// <summary>
/// Browser automation abstraction. Simple navigation/search runs in the user's own browser and profile.
/// Page reading, clicking and typing use a Chrome DevTools session (<see cref="CdpSession"/>) in a separate Astra profile.
/// </summary>
public sealed class BrowserController : IDisposable
{
    private readonly SearchService _search;
    private readonly SettingsStore _store;
    private readonly WindowManager _windows;
    private static readonly HttpClient Http = CreateHttp();

    public CdpSession? Cdp { get; private set; }

    public BrowserController(SearchService search, SettingsStore store, WindowManager windows)
    {
        _search = search;
        _store = store;
        _windows = windows;
    }

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        h.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,tr;q=0.8");
        return h;
    }

    // ---- Resolving which browser ------------------------------------------

    public AppHit? ResolveBrowser(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var hit = _search.SearchApplications(name, 3, "browser").FirstOrDefault()
                      ?? _search.SearchApplications(name, 3).FirstOrDefault(h => IsBrowserExe(h.ExePath));
            if (hit is not null) return hit;
        }
        var pref = _store.Current.Automation.PreferredBrowserId;
        if (!string.IsNullOrEmpty(pref))
        {
            var hit = _search.SearchApplications(pref, 3, "browser").FirstOrDefault();
            if (hit is not null) return hit;
        }
        var def = DefaultBrowserExe();
        if (def is not null)
        {
            var all = _search.SearchApplications(Path.GetFileNameWithoutExtension(def), 5, "browser");
            var hit = all.FirstOrDefault(h => string.Equals(h.ExePath, def, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault();
            if (hit is not null) return hit;
        }
        return _search.SearchApplications("browser", 5, "browser").FirstOrDefault();
    }

    private static bool IsBrowserExe(string? exe) =>
        exe is not null && new[] { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "avast" }.Any(b =>
            Path.GetFileNameWithoutExtension(exe).Contains(b, StringComparison.OrdinalIgnoreCase));

    private static string? DefaultBrowserExe()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            var progId = k?.GetValue("ProgId") as string;
            if (progId is null) return null;
            using var cmd = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var line = cmd?.GetValue(null) as string;
            if (line is null) return null;
            var m = Regex.Match(line, "^\"([^\"]+)\"|^(\\S+)");
            return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : null;
        }
        catch { return null; }
    }

    public static bool IsChromium(string? exe) =>
        exe is not null && !Path.GetFileName(exe).Contains("firefox", StringComparison.OrdinalIgnoreCase);

    // ---- URLs --------------------------------------------------------------

    private static readonly Dictionary<string, string> KnownSites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com", ["github"] = "https://github.com", ["google"] = "https://www.google.com",
        ["gmail"] = "https://mail.google.com", ["twitter"] = "https://x.com", ["x"] = "https://x.com", ["reddit"] = "https://www.reddit.com",
        ["wikipedia"] = "https://www.wikipedia.org", ["netflix"] = "https://www.netflix.com", ["twitch"] = "https://www.twitch.tv",
        ["spotify"] = "https://open.spotify.com", ["instagram"] = "https://www.instagram.com", ["facebook"] = "https://www.facebook.com",
        ["linkedin"] = "https://www.linkedin.com", ["amazon"] = "https://www.amazon.com", ["chatgpt"] = "https://chatgpt.com",
        ["discord"] = "https://discord.com/app", ["whatsapp"] = "https://web.whatsapp.com", ["trendyol"] = "https://www.trendyol.com",
        ["hepsiburada"] = "https://www.hepsiburada.com", ["maps"] = "https://maps.google.com", ["drive"] = "https://drive.google.com",
    };

    public static string NormalizeUrl(string input)
    {
        var s = input.Trim();
        if (Regex.IsMatch(s, "^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)) return s;
        if (KnownSites.TryGetValue(s, out var known)) return known;
        if (!s.Contains(' ') && s.Contains('.') && Regex.IsMatch(s, @"^[\w.-]+\.[a-z]{2,}(/.*)?$", RegexOptions.IgnoreCase)) return "https://" + s;
        return SearchUrl(s, null);
    }

    public static string SearchUrl(string query, string? engine)
    {
        var q = Uri.EscapeDataString(query);
        return (engine ?? "google").ToLowerInvariant() switch
        {
            "youtube" => $"https://www.youtube.com/results?search_query={q}",
            "bing" => $"https://www.bing.com/search?q={q}",
            "duckduckgo" or "ddg" => $"https://duckduckgo.com/?q={q}",
            "github" => $"https://github.com/search?q={q}",
            "wikipedia" => $"https://en.wikipedia.org/w/index.php?search={q}",
            "maps" => $"https://www.google.com/maps/search/{q}",
            _ => $"https://www.google.com/search?q={q}",
        };
    }

    // ---- Launching / navigating -------------------------------------------

    public sealed record OpenResult(bool Started, bool Verified, string Message, string BrowserName);

    /// <summary>Opens a URL in the chosen browser and verifies that a browser window shows the expected page title.</summary>
    public async Task<OpenResult> OpenUrlAsync(string url, string? browserName, string? titleHint, CancellationToken ct = default)
    {
        var browser = ResolveBrowser(browserName);
        if (browser is null) return new(false, false, "No browser was found in the computer index.", "");
        var exe = browser.ExePath;
        var before = _windows.List().Where(w => exe is not null && w.ProcessName.Equals(Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Title).ToHashSet();
        try
        {
            if (exe is not null)
            {
                var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
                var profile = ProfileFor(browser);
                if (profile is not null && IsChromium(exe)) psi.ArgumentList.Add($"--profile-directory={profile}");
                psi.ArgumentList.Add(url);
                Process.Start(psi);
            }
            else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) { return new(false, false, $"Could not start {browser.Name}: {ex.Message}", browser.Name); }

        var hint = (titleHint ?? HintFromUrl(url)).ToLowerInvariant();
        var procName = exe is not null ? Path.GetFileNameWithoutExtension(exe) : "";
        bool IsBrowserWindow(WindowInfo w) => procName.Length == 0 || w.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase);
        bool MatchesHint(WindowInfo w) => hint.Length == 0 || TextNorm.Normalize(w.Title).Contains(TextNorm.Normalize(hint));

        // The page title must actually change to the expected one; an old tab that already matched doesn't count.
        var win = await _windows.WaitForWindowAsync(w => IsBrowserWindow(w) && MatchesHint(w) && !before.Contains(w.Title), 10000, ct);
        if (win is not null) return new(true, true, $"{browser.Name} is showing “{win.Title}”.", browser.Name);
        var unchanged = _windows.List().FirstOrDefault(w => IsBrowserWindow(w) && MatchesHint(w));
        if (unchanged is not null) return new(true, true, $"{browser.Name} already shows “{unchanged.Title}”.", browser.Name);

        var any = _windows.List().FirstOrDefault(w => w.ProcessName.Equals(procName, StringComparison.OrdinalIgnoreCase));
        return any is not null
            ? new(true, false, $"{browser.Name} is open (“{any.Title}”), but the page title did not confirm the expected site yet.", browser.Name)
            : new(true, false, $"{browser.Name} was started, but no window was detected.", browser.Name);
    }

    private string? ProfileFor(AppHit browser)
    {
        var profiles = _search.BrowserProfiles(browser.Name);
        if (profiles.Count == 0) return null;
        // Prefer the profile Chromium itself calls "Default"; otherwise the first one.
        return (profiles.FirstOrDefault(p => p.ProfileDir == "Default").ProfileDir) ?? profiles[0].ProfileDir;
    }

    private static string HintFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return "";
        var host = u.Host.Replace("www.", "");
        if (host.Contains("google.") && u.Query.Contains("q=")) return Uri.UnescapeDataString(Regex.Match(u.Query, @"[?&]q=([^&]+)").Groups[1].Value.Replace('+', ' '));
        if (host.Contains("youtube") && u.Query.Contains("search_query=")) return Uri.UnescapeDataString(Regex.Match(u.Query, @"search_query=([^&]+)").Groups[1].Value.Replace('+', ' '));
        return host.Split('.')[0];
    }

    // ---- YouTube -----------------------------------------------------------

    public sealed record VideoHit(string Id, string Title, string Url);

    /// <summary>Finds the first real video for a query by reading YouTube's results page (no ads, no API key).</summary>
    public async Task<VideoHit?> FirstYouTubeVideoAsync(string query, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}&hl=en");
        req.Headers.Add("Cookie", "CONSENT=YES+cb; SOCS=CAI");
        using var res = await Http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        var html = await res.Content.ReadAsStringAsync(ct);
        // videoRenderer blocks hold the organic results; ads use different renderers.
        var m = Regex.Match(html, "\"videoRenderer\":\\{\"videoId\":\"([\\w-]{11})\".*?\"title\":\\{\"runs\":\\[\\{\"text\":\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline);
        if (!m.Success) return null;
        var title = Regex.Unescape(m.Groups[2].Value);
        return new VideoHit(m.Groups[1].Value, title, $"https://www.youtube.com/watch?v={m.Groups[1].Value}");
    }

    // ---- Advanced (DevTools) session --------------------------------------

    public async Task<CdpSession> EnsureSessionAsync(string? browserName, CancellationToken ct = default)
    {
        if (Cdp is { IsAlive: true }) return Cdp;
        var browser = ResolveBrowser(browserName) ?? throw new InvalidOperationException("No browser found.");
        if (!IsChromium(browser.ExePath) || browser.ExePath is null)
            throw new InvalidOperationException($"{browser.Name} does not support the DevTools protocol. Use a Chromium-based browser (Chrome, Edge, Brave, Avast Secure Browser).");
        Cdp = await CdpSession.StartAsync(browser.ExePath, Path.Combine(SettingsStore.DataDirectory, "browser-profile", TextNorm.Normalize(browser.Name).Replace(' ', '-')), ct);
        return Cdp;
    }

    public void Dispose() => Cdp?.Dispose();
}
