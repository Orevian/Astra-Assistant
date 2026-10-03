using Astra.Core.Localization;

namespace Astra.Core.Native;

public sealed record DisplayInfo(string DeviceName, int Index, bool IsPrimary, int X, int Y, int Width, int Height,
    int WorkX, int WorkY, int WorkWidth, int WorkHeight, double Scale)
{
    public string Label => IsPrimary
        ? $"{Loc.F("Display {0}", Index)} · {Loc.T("Primary")} ({Width}×{Height})"
        : $"{Loc.F("Display {0}", Index)} ({Width}×{Height})";
}

/// <summary>Monitor enumeration. An empty/unknown device name always resolves to the primary display.</summary>
public static class Displays
{
    public static IReadOnlyList<DisplayInfo> All()
    {
        var list = new List<DisplayInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var mi = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(h, ref mi)) return true;
            double scale = 1;
            if (NativeMethods.GetDpiForMonitor(h, 0, out var dx, out _) == 0) scale = dx / 96.0;
            var name = mi.szDevice;
            // "\\.\DISPLAY2" → 2, so labels match the numbers shown in Windows display settings.
            var digits = new string(name.Where(char.IsDigit).ToArray());
            var index = int.TryParse(digits, out var n) ? n : list.Count + 1;
            list.Add(new DisplayInfo(name, index, (mi.dwFlags & 1) != 0,
                mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Width, mi.rcMonitor.Height,
                mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Width, mi.rcWork.Height, scale));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(d => !d.IsPrimary).ThenBy(d => d.Index).ToList();
    }

    public static DisplayInfo Primary() => All().FirstOrDefault(d => d.IsPrimary) ?? All().First();

    public static DisplayInfo Resolve(string? deviceName)
    {
        var all = All();
        if (!string.IsNullOrEmpty(deviceName))
        {
            var match = all.FirstOrDefault(d => d.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return all.FirstOrDefault(d => d.IsPrimary) ?? all[0];
    }

    public static DisplayInfo FromPoint(int x, int y) =>
        All().FirstOrDefault(d => x >= d.X && x < d.X + d.Width && y >= d.Y && y < d.Y + d.Height) ?? Primary();

    /// <summary>Top-left pixel for a window of the given physical size placed in a corner of a display's work area.</summary>
    public static (int X, int Y) Place(DisplayInfo d, Settings.ScreenCorner corner, int width, int height, int margin, int offsetY = 0)
    {
        var m = (int)(margin * d.Scale);
        var left = d.WorkX + m;
        var right = d.WorkX + d.WorkWidth - width - m;
        var top = d.WorkY + m;
        var bottom = d.WorkY + d.WorkHeight - height - m;
        return corner switch
        {
            Settings.ScreenCorner.TopLeft => (left, top + offsetY),
            Settings.ScreenCorner.TopRight => (right, top + offsetY),
            Settings.ScreenCorner.BottomLeft => (left, bottom - offsetY),
            Settings.ScreenCorner.BottomRight => (right, bottom - offsetY),
            _ => (d.WorkX + (d.WorkWidth - width) / 2, d.WorkY + (d.WorkHeight - height) / 2),
        };
    }
}
