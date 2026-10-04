using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astra.Core.SystemInfo;
using Microsoft.Win32;

namespace Astra.Core.Index;

public sealed record AppRecord(string Name, string Kind, string LaunchKind, string Launch, string? ExePath,
    string? Publisher, string? Version, string Source);

public sealed record BrowserProfile(string BrowserId, string BrowserName, string? ExePath, string ProfileDir, string ProfileName);

/// <summary>Discovers installed apps: Start Menu, UWP, uninstall registry, App Paths, Steam/Epic games and browsers.</summary>
public static class ApplicationScanner
{
    private static readonly string[] SkipWords =
    {
        "uninstall", "kaldır", "readme", "release notes", "documentation", "dokümantasyon", "license", "lisans",
        "website", "web sitesi", "help", "yardım", "manual", "kılavuz", "what's new", "changelog",
    };

    public static List<AppRecord> Scan(CancellationToken ct = default)
    {
        var found = new Dictionary<string, AppRecord>();
        void Add(AppRecord r)
        {
            var key = TextNorm.Normalize(r.Name);
            if (key.Length == 0 || found.ContainsKey(key)) return;
            found[key] = r;
        }

        var browsers = BrowserDetector.Detect();
        foreach (var b in browsers.Where(b => b.ExecutablePath is not null))
            Add(new AppRecord(b.Name, "browser", "file", b.ExecutablePath!, b.ExecutablePath, null, null, "browser"));

        foreach (var r in ScanStartMenu(ct)) Add(r);
        ct.ThrowIfCancellationRequested();
        foreach (var r in ScanUwp(ct)) Add(r);
        foreach (var r in ScanSteam()) Add(r);
        foreach (var r in ScanEpic()) Add(r);
        ct.ThrowIfCancellationRequested();
        foreach (var r in ScanUninstallRegistry()) Add(r);
        foreach (var r in ScanAppPaths()) Add(r);

        var browserExes = browsers.Select(b => b.ExecutablePath).Where(p => p is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return found.Values
            .Select(r => r.ExePath is not null && browserExes.Contains(r.ExePath) ? r with { Kind = "browser" } : r)
            .ToList();
    }

    // ---- Start Menu --------------------------------------------------------

    private static IEnumerable<AppRecord> ScanStartMenu(CancellationToken ct)
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> links;
            try { links = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch { continue; }
            foreach (var lnk in links)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (SkipWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                var target = ResolveShortcut(lnk);
                if (!string.IsNullOrEmpty(target))
                {
                    var ext = Path.GetExtension(target).ToLowerInvariant();
                    if (ext is not (".exe" or ".msc" or ".cpl" or ".bat" or ".cmd")) continue;
                }
                var exe = target is { Length: > 0 } && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : null;
                var kind = exe is not null && exe.Contains("\\steamapps\\", StringComparison.OrdinalIgnoreCase) ? "game" : "app";
                yield return new AppRecord(name, kind, "file", lnk, exe, null, null, "startmenu");
            }
        }
    }

    private static string? ResolveShortcut(string path)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            var sb = new StringBuilder(520);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            Marshal.FinalReleaseComObject(link);
            return sb.Length > 0 ? sb.ToString() : null;
        }
        catch { return null; }
    }

    // ---- UWP / Store apps --------------------------------------------------

    private static IEnumerable<AppRecord> ScanUwp(CancellationToken ct)
    {
        var json = RunPowerShell("[Console]::OutputEncoding=[Text.Encoding]::UTF8; Get-StartApps | ConvertTo-Json -Compress", 25000);
        if (string.IsNullOrWhiteSpace(json)) yield break;
        List<(string name, string id)> items = new();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new() { doc.RootElement };
            foreach (var e in arr)
            {
                var n = e.TryGetProperty("Name", out var nv) ? nv.GetString() : null;
                var id = e.TryGetProperty("AppID", out var iv) ? iv.GetString() : null;
                if (n is not null && id is not null && id.Contains('!')) items.Add((n, id));
            }
        }
        catch { yield break; }
        foreach (var (name, id) in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return new AppRecord(name, "app", "uwp", id, null, null, null, "uwp");
        }
    }

    internal static string? RunPowerShell(string command, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return null; }
            return output.Result;
        }
        catch { return null; }
    }

    // ---- Games -------------------------------------------------------------

    private static IEnumerable<AppRecord> ScanSteam()
    {
        string? steam = null;
        try { steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; } catch { }
        if (steam is null || !Directory.Exists(steam)) yield break;
        steam = steam.Replace('/', '\\');

        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace("\\\\", "\\"));

        var skip = new HashSet<string> { "228980", "250820" }; // Steamworks redistributables, SteamVR
        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                string text;
                try { text = File.ReadAllText(acf); } catch { continue; }
                var id = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                var name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                if (id.Length == 0 || name.Length == 0 || skip.Contains(id)) continue;
                yield return new AppRecord(name, "game", "uri", $"steam://rungameid/{id}", null, "Steam", null, "steam");
            }
        }
    }

    private static IEnumerable<AppRecord> ScanEpic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var item in Directory.EnumerateFiles(dir, "*.item"))
        {
            AppRecord? rec = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var r = doc.RootElement;
                var name = r.GetProperty("DisplayName").GetString();
                var app = r.GetProperty("AppName").GetString();
                if (name is not null && app is not null)
                    rec = new AppRecord(name, "game", "uri", $"com.epicgames.launcher://apps/{app}?action=launch&silent=true", null, "Epic Games", null, "epic");
            }
            catch { }
            if (rec is not null) yield return rec;
        }
    }

    // ---- Registry ----------------------------------------------------------

    private static IEnumerable<AppRecord> ScanUninstallRegistry()
    {
        var keys = new (RegistryHive, RegistryView, string)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };
        foreach (var (hive, view, sub) in keys)
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(sub);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                AppRecord? rec = null;
                try
                {
                    using var k = root.OpenSubKey(name);
                    var display = k?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display) || k!.GetValue("SystemComponent") is 1) continue;
                    if (k.GetValue("ParentKeyName") is not null) continue;
                    if (SkipWords.Any(w => display.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

                    var exe = ExeFromIcon(k.GetValue("DisplayIcon") as string);
                    if (exe is null && k.GetValue("InstallLocation") is string loc && Directory.Exists(loc))
                        exe = GuessMainExe(loc, display);
                    if (exe is null) continue;
                    rec = new AppRecord(display, "app", "file", exe, exe, k.GetValue("Publisher") as string,
                        k.GetValue("DisplayVersion") as string, "registry");
                }
                catch { }
                if (rec is not null) yield return rec;
            }
        }
    }

    private static string? ExeFromIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var path = icon.Split(',')[0].Trim().Trim('"');
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)
               && !Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static string? GuessMainExe(string dir, string displayName)
    {
        try
        {
            var norm = TextNorm.Normalize(displayName).Replace(" ", "");
            var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !Path.GetFileName(f).StartsWith("unins", StringComparison.OrdinalIgnoreCase)).ToList();
            return exes.FirstOrDefault(f => TextNorm.Normalize(Path.GetFileNameWithoutExtension(f)).Replace(" ", "") is { Length: > 2 } n
                                            && (norm.Contains(n) || n.Contains(norm)));
        }
        catch { return null; }
    }

    private static IEnumerable<AppRecord> ScanAppPaths()
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var root = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames().Where(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                AppRecord? rec = null;
                try
                {
                    using var k = root.OpenSubKey(name);
                    var path = (k?.GetValue(null) as string)?.Trim('"');
                    if (path is { Length: > 0 } && File.Exists(path))
                        rec = new AppRecord(Path.GetFileNameWithoutExtension(name), "app", "file", path, path, null, null, "apppaths");
                }
                catch { }
                if (rec is not null) yield return rec;
            }
        }
    }

    // ---- Browser profiles --------------------------------------------------

    public static List<BrowserProfile> ScanBrowserProfiles()
    {
        var result = new List<BrowserProfile>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var chromium = new (string Id, string Match, string Root)[]
        {
            ("chrome", "chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("edge", "edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("brave", "brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data")),
            ("vivaldi", "vivaldi", Path.Combine(local, "Vivaldi", "User Data")),
            ("avast", "avast", Path.Combine(local, "AVAST Software", "Browser", "User Data")),
        };
        var browsers = BrowserDetector.Detect();
        string? ExeFor(string match) => browsers.FirstOrDefault(b => b.Name.Contains(match, StringComparison.OrdinalIgnoreCase))?.ExecutablePath;
        string NameFor(string match, string fallback) => browsers.FirstOrDefault(b => b.Name.Contains(match, StringComparison.OrdinalIgnoreCase))?.Name ?? fallback;

        foreach (var (id, match, root) in chromium)
        {
            var state = Path.Combine(root, "Local State");
            if (!File.Exists(state)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(state));
                if (!doc.RootElement.TryGetProperty("profile", out var p) || !p.TryGetProperty("info_cache", out var cache)) continue;
                foreach (var prof in cache.EnumerateObject())
                {
                    var pname = prof.Value.TryGetProperty("name", out var n) ? n.GetString() ?? prof.Name : prof.Name;
                    result.Add(new BrowserProfile(id, NameFor(match, id), ExeFor(match), prof.Name, pname));
                }
            }
            catch { }
        }

        var ini = Path.Combine(roaming, "Mozilla", "Firefox", "profiles.ini");
        if (File.Exists(ini))
        {
            string? curName = null, curPath = null;
            void Flush() { if (curName is not null && curPath is not null) result.Add(new BrowserProfile("firefox", NameFor("firefox", "Firefox"), ExeFor("firefox"), curPath, curName)); curName = curPath = null; }
            foreach (var line in File.ReadLines(ini))
            {
                if (line.StartsWith("[Profile")) Flush();
                else if (line.StartsWith("Name=")) curName = line[5..];
                else if (line.StartsWith("Path=")) curPath = line[5..];
            }
            Flush();
        }
        return result;
    }

    // ---- Shell link COM ----------------------------------------------------

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
