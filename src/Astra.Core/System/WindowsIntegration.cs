using Microsoft.Win32;

namespace Astra.Core.SystemInfo;

public sealed record BrowserInfo(string Id, string Name, string? ExecutablePath);

public static class BrowserDetector
{
    /// <summary>Reads the registered browsers (Settings ▸ Default apps) from the registry.</summary>
    public static IReadOnlyList<BrowserInfo> Detect()
    {
        var found = new Dictionary<string, BrowserInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var root = baseKey.OpenSubKey(@"SOFTWARE\Clients\StartMenuInternet");
                    if (root is null) continue;
                    foreach (var sub in root.GetSubKeyNames())
                    {
                        using var k = root.OpenSubKey(sub);
                        var name = k?.GetValue(null) as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        using var cmd = k!.OpenSubKey(@"shell\open\command");
                        var exe = (cmd?.GetValue(null) as string)?.Trim().Trim('"');
                        if (exe is not null && !File.Exists(exe)) exe = null;
                        found.TryAdd(name, new BrowserInfo(sub, name, exe));
                    }
                }
                catch { /* hive not readable: ignore */ }
            }
        }
        return found.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Astra";

    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled, string? exePath = null)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            exePath ??= Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unknown.");
            k.SetValue(ValueName, $"\"{exePath}\" --minimized");
        }
        else
        {
            k.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
