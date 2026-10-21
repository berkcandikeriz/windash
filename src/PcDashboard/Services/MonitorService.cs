using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PcDashboard.Core;
using PcDashboard.Interop;

namespace PcDashboard.Services;

/// <param name="Work">Görev çubuğu hariç kullanılabilir alan (fiziksel piksel).</param>
public sealed record MonitorInfo(string DeviceName, int X, int Y, int Width, int Height, bool IsPrimary, IntPtr Handle,
    Int32Rect Work)
{
    public override string ToString() =>
        $"{DeviceName} {Width}x{Height} @ ({X},{Y}){(IsPrimary ? " [ana ekran]" : "")}";
}

/// <summary>Pencereyi doğru monitöre, tam ekran olarak yerleştirir.</summary>
public static class MonitorService
{
    public static List<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data)
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
            {
                var r = info.rcMonitor;
                var w = info.rcWork;
                list.Add(new MonitorInfo(info.szDevice, r.Left, r.Top, r.Width, r.Height,
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0, hMonitor,
                    new Int32Rect(w.Left, w.Top, w.Width, w.Height)));
            }

            return true;
        }

        NativeMethods.MonitorEnumProc proc = Callback;
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return list;
    }

    public static MonitorInfo? Choose(IReadOnlyList<MonitorInfo> monitors, DisplaySettings s)
    {
        if (monitors.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(s.MonitorDeviceName))
        {
            var named = monitors.FirstOrDefault(m =>
                m.DeviceName.Equals(s.MonitorDeviceName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named;
            Log.Warn($"'{s.MonitorDeviceName}' adlı monitör bulunamadı, otomatik seçim yapılıyor.");
        }

        if (s.PreferSecondary)
        {
            var secondary = monitors.Where(m => !m.IsPrimary).ToList();
            return secondary.FirstOrDefault(m => m.Width == s.PreferredWidth && m.Height == s.PreferredHeight)
                   ?? secondary.FirstOrDefault()
                   ?? monitors.FirstOrDefault(m => m.IsPrimary)
                   ?? monitors[0];
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>Pencereyi monitörün tamamını kaplayacak şekilde fiziksel piksellerle konumlandırır.</summary>
    public static void Place(Window window, MonitorInfo monitor) =>
        Place(window, new Int32Rect(monitor.X, monitor.Y, monitor.Width, monitor.Height));

    /// <summary>Pencereyi verilen fiziksel piksel dikdörtgenine yerleştirir.</summary>
    public static void Place(Window window, Int32Rect rect)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || rect.Width <= 0 || rect.Height <= 0) return;

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
    }

    /// <summary>Pencerenin şu an üzerinde durduğu monitör.</summary>
    public static MonitorInfo? Current(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return null;
        var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return GetMonitors().FirstOrDefault(m => m.Handle == handle);
    }

    /// <summary>Pencerenin fiziksel piksel cinsinden konumu ve boyutu.</summary>
    public static Int32Rect? Bounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var r)) return null;
        return new Int32Rect(r.Left, r.Top, r.Width, r.Height);
    }
}
