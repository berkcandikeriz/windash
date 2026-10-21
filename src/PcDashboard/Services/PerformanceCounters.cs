using System.Runtime.InteropServices;
using PcDashboard.Core;
using PcDashboard.Interop;

namespace PcDashboard.Services;

/// <summary>
/// CPU ve GPU kullanımını Windows performans sayaçlarından (PDH) okur.
/// Görev Yöneticisi'nin kullandığı sayaçlarla aynıdır.
/// </summary>
public sealed class PerformanceCounters : IDisposable
{
    private IntPtr _query;
    private readonly IntPtr _cpu;
    private readonly IntPtr _gpu;
    private readonly bool _cpuOk;
    private readonly bool _gpuOk;

    public PerformanceCounters()
    {
        try
        {
            if (NativeMethods.PdhOpenQuery(null, IntPtr.Zero, out _query) != 0)
            {
                _query = IntPtr.Zero;
                return;
            }

            _cpuOk = NativeMethods.PdhAddEnglishCounter(_query,
                @"\Processor Information(_Total)\% Processor Utility", IntPtr.Zero, out _cpu) == 0;
            if (!_cpuOk)
            {
                _cpuOk = NativeMethods.PdhAddEnglishCounter(_query,
                    @"\Processor(_Total)\% Processor Time", IntPtr.Zero, out _cpu) == 0;
            }

            _gpuOk = NativeMethods.PdhAddEnglishCounter(_query,
                @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpu) == 0;

            NativeMethods.PdhCollectQueryData(_query);
        }
        catch (Exception ex)
        {
            Log.Error("Performans sayaçları açılamadı", ex);
            _query = IntPtr.Zero;
        }
    }

    public (double? Cpu, double? Gpu) Sample()
    {
        if (_query == IntPtr.Zero) return (null, null);
        if (NativeMethods.PdhCollectQueryData(_query) != 0) return (null, null);
        return (_cpuOk ? ReadCpu() : null, _gpuOk ? ReadGpu() : null);
    }

    private double? ReadCpu()
    {
        var status = NativeMethods.PdhGetFormattedCounterValue(_cpu,
            NativeMethods.PDH_FMT_DOUBLE | NativeMethods.PDH_FMT_NOCAP100, out _, out var value);
        if (status != 0 || value.CStatus > 1) return null;
        return Math.Clamp(value.doubleValue, 0, 100);
    }

    /// <summary>
    /// GPU motorları (3D, Copy, VideoDecode...) ayrı ayrı raporlanır. Görev Yöneticisi gibi
    /// her motor türünü toplayıp en yüksek olanı alıyoruz.
    /// </summary>
    private double? ReadGpu()
    {
        uint size = 0, count = 0;
        var status = NativeMethods.PdhGetFormattedCounterArray(_gpu, NativeMethods.PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
        if (status != NativeMethods.PDH_MORE_DATA || size == 0) return null;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = NativeMethods.PdhGetFormattedCounterArray(_gpu, NativeMethods.PDH_FMT_DOUBLE, ref size, ref count, buffer);
            if (status != 0) return null;

            var itemSize = Marshal.SizeOf<NativeMethods.PDH_FMT_COUNTERVALUE_ITEM>();
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<NativeMethods.PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.FmtValue.CStatus > 1) continue;

                var name = Marshal.PtrToStringUni(item.szName) ?? "";
                var idx = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                var engine = idx >= 0 ? name[(idx + 8)..] : "other";
                totals[engine] = totals.GetValueOrDefault(engine) + item.FmtValue.doubleValue;
            }

            return totals.Count == 0 ? 0 : Math.Clamp(totals.Values.Max(), 0, 100);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            NativeMethods.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
