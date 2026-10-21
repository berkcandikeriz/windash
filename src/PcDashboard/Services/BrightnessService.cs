using System.Runtime.InteropServices;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>
/// Harici monitörlerin parlaklığını DDC/CI üzerinden (VCP 0x10) okur ve ayarlar.
/// DDC/CI çağrıları yavaştır (monitör başına ~50-100 ms) ve UI'ı bekletmemek için arka planda,
/// tek bir iş parçacığında sırayla yapılır. Kaydırıcı hızlı oynatılırsa yalnızca son değer gönderilir.
/// Not: Monitörün menüsünden DDC/CI açık olmalı. USB-C ile bağlanan bazı taşınabilir ekranlar desteklemez.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private const byte VcpBrightness = 0x10;

    private readonly string _target;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<PhysicalMonitor> _monitors = new();
    private readonly object _sync = new();
    private int? _pending;
    private bool _writerRunning;

    public BrightnessService(string target) => _target = string.IsNullOrWhiteSpace(target) ? "all" : target.Trim();

    public bool IsSupported => _monitors.Count > 0;

    /// <summary>Monitörleri bulur ve ilk desteklenen monitörün parlaklığını (0-100) döndürür.</summary>
    public async Task<int?> InitializeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                Release();
                foreach (var monitor in MonitorService.GetMonitors().Where(Matches))
                {
                    foreach (var pm in Open(monitor.Handle))
                    {
                        if (ReadPercent(pm) is not null) _monitors.Add(pm);
                        else NativeDxva2.DestroyPhysicalMonitor(pm.Handle);
                    }
                }

                Log.Info($"Parlaklık: DDC/CI destekleyen {_monitors.Count} monitör bulundu");
                return _monitors.Count > 0 ? ReadPercent(_monitors[0]) : null;
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int?> ReadAsync()
    {
        if (_monitors.Count == 0) return null;
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() => ReadPercent(_monitors[0]));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Yeni değeri kuyruğa koyar; önceki yazma sürerken gelen değerlerden yalnızca sonuncusu uygulanır.</summary>
    public void Set(int percent)
    {
        if (_monitors.Count == 0) return;
        lock (_sync)
        {
            _pending = Math.Clamp(percent, 0, 100);
            if (_writerRunning) return;
            _writerRunning = true;
        }

        _ = Task.Run(WriterLoopAsync);
    }

    private async Task WriterLoopAsync()
    {
        while (true)
        {
            int value;
            lock (_sync)
            {
                if (_pending is null)
                {
                    _writerRunning = false;
                    return;
                }

                value = _pending.Value;
                _pending = null;
            }

            await _gate.WaitAsync();
            try
            {
                foreach (var pm in _monitors) WritePercent(pm, value);
            }
            catch (Exception ex)
            {
                Log.Error("Parlaklık ayarlanamadı", ex);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private bool Matches(MonitorInfo m) => _target.ToLowerInvariant() switch
    {
        "all" => true,
        "primary" => m.IsPrimary,
        _ => m.DeviceName.Equals(_target, StringComparison.OrdinalIgnoreCase),
    };

    private static IEnumerable<PhysicalMonitor> Open(IntPtr hMonitor)
    {
        if (!NativeDxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
            return Array.Empty<PhysicalMonitor>();

        var array = new NativeDxva2.PHYSICAL_MONITOR[count];
        if (!NativeDxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
            return Array.Empty<PhysicalMonitor>();

        return array.Select(a => new PhysicalMonitor(a.hPhysicalMonitor, a.szPhysicalMonitorDescription)).ToList();
    }

    private static int? ReadPercent(PhysicalMonitor pm)
    {
        if (!NativeDxva2.GetVCPFeatureAndVCPFeatureReply(pm.Handle, VcpBrightness, IntPtr.Zero, out var current, out var max) || max == 0)
            return null;
        return (int)Math.Round(current * 100.0 / max);
    }

    private static void WritePercent(PhysicalMonitor pm, int percent)
    {
        if (!NativeDxva2.GetVCPFeatureAndVCPFeatureReply(pm.Handle, VcpBrightness, IntPtr.Zero, out _, out var max) || max == 0)
            max = 100;
        var value = (uint)Math.Round(percent / 100.0 * max);
        if (!NativeDxva2.SetVCPFeature(pm.Handle, VcpBrightness, value))
            Log.Warn($"Parlaklık yazılamadı: {pm.Description}");
    }

    private void Release()
    {
        foreach (var pm in _monitors) NativeDxva2.DestroyPhysicalMonitor(pm.Handle);
        _monitors.Clear();
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            Release();
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record PhysicalMonitor(IntPtr Handle, string Description);

    private static class NativeDxva2
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte vcpCode, IntPtr vcpType, out uint currentValue, out uint maximumValue);

        [DllImport("dxva2.dll", SetLastError = true)]
        public static extern bool SetVCPFeature(IntPtr hMonitor, byte vcpCode, uint newValue);
    }
}
