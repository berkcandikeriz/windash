using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PcDashboard.Core;
using PcDashboard.Interop;
using PcDashboard.Services;

namespace PcDashboard.Widgets.SystemInfo;

public sealed partial class SystemMetric : ObservableObject
{
    public SystemMetric(string label) => Label = label;

    public string Label { get; }

    [ObservableProperty] private string _value = "—";
    [ObservableProperty] private double _percent;
}

public sealed partial class SystemInfoViewModel : WidgetViewModel
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly SystemMonitorSettings _settings;
    private readonly PerformanceCounters _counters = new();
    private readonly SystemMetric _cpu = new("CPU");
    private readonly SystemMetric _ram = new("RAM");
    private readonly SystemMetric _gpu = new("GPU");
    private readonly List<(DriveInfo Drive, SystemMetric Metric)> _drives = new();

    public ObservableCollection<SystemMetric> Metrics { get; } = new();

    /// <summary>Taslaktaki 2x2 kompakt görünüm (başlıksız, değerler boşluksuz).</summary>
    public bool Compact { get; }

    private string Sep => Compact ? "/" : " / ";

    public SystemInfoViewModel(SystemMonitorSettings settings, bool compact = false)
    {
        _settings = settings;
        Compact = compact;
        Metrics.Add(_cpu);
        Metrics.Add(_ram);
        Metrics.Add(_gpu);

        var drives = _settings.Drives.Count > 0
            ? _settings.Drives
            : new List<string> { Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\" };
        foreach (var path in drives)
        {
            try
            {
                var drive = new DriveInfo(path);
                var metric = new SystemMetric($"Disk {drive.Name.TrimEnd('\\')}");
                _drives.Add((drive, metric));
                Metrics.Add(metric);
            }
            catch (Exception ex)
            {
                Log.Warn($"Disk eklenemedi '{path}': {ex.Message}");
            }
        }
    }

    public override Task StartAsync()
    {
        Every(TimeSpan.FromSeconds(2), SampleFast);
        Every(TimeSpan.FromMinutes(1), SampleDrives);
        return Task.CompletedTask;
    }

    private void SampleFast()
    {
        var (cpu, gpu) = _counters.Sample();
        Set(_cpu, cpu);
        Set(_gpu, gpu);

        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            var total = mem.ullTotalPhys / 1024d / 1024 / 1024;
            var used = (mem.ullTotalPhys - mem.ullAvailPhys) / 1024d / 1024 / 1024;
            _ram.Value = $"{used.ToString("0.0", Tr)}{Sep}{Math.Round(total)} GB";
            _ram.Percent = used / total * 100;
        }
    }

    private void SampleDrives()
    {
        foreach (var (drive, metric) in _drives)
        {
            try
            {
                if (!drive.IsReady)
                {
                    metric.Value = "hazır değil";
                    metric.Percent = 0;
                    continue;
                }

                var total = drive.TotalSize / 1024d / 1024 / 1024;
                var used = (drive.TotalSize - drive.TotalFreeSpace) / 1024d / 1024 / 1024;
                metric.Value = $"{Math.Round(used)}{Sep}{Math.Round(total)} GB";
                metric.Percent = used / total * 100;
            }
            catch (Exception ex)
            {
                metric.Value = "okunamadı";
                Log.Warn($"Disk okunamadı {drive.Name}: {ex.Message}");
            }
        }
    }

    private static void Set(SystemMetric metric, double? percent)
    {
        if (percent is null)
        {
            metric.Value = "—";
            metric.Percent = 0;
            return;
        }

        metric.Value = $"%{Math.Round(percent.Value)}";
        metric.Percent = percent.Value;
    }

    protected override void OnDisposing() => _counters.Dispose();
}
