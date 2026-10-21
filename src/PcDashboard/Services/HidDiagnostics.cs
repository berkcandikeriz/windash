using System.Text;
using HidSharp;
using HidSharp.Reports;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>
/// Keychron klavye / alıcının bilgisayara neler gösterdiğini günlüğe yazar:
/// HID arayüzleri, kullanımları, standart pil alanı (Battery Strength 0x06/0x20) olup olmadığı,
/// Bluetooth pil bilgisi ve 15 saniye boyunca okunabilen arayüzlerden gelen ham raporlar.
/// Sonuç %LOCALAPPDATA%\WinDash\log.txt içine "[HID TANI]" başlığıyla yazılır.
/// </summary>
public static class HidDiagnostics
{
    private const uint UsageBatteryStrength = 0x00060020;
    private const uint UsageRelativeStateOfCharge = 0x00850066;
    private const uint UsageAbsoluteStateOfCharge = 0x00850065;

    public static Task RunKeychronAsync(TimeSpan listenFor) =>
        RunAsync(KeychronBatteryMonitor.KeychronVendorId, "Keychron", listenFor);

    /// <summary>Verilen üreticinin (VID) tüm HID arayüzlerini inceler; adında nameHint geçenler de dahil edilir.</summary>
    public static Task RunAsync(int vendorId, string nameHint, TimeSpan listenFor) => Task.Run(async () =>
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== [HID TANI] başladı ==========");

        List<HidDevice> devices;
        try
        {
            devices = DeviceList.Local.GetHidDevices()
                .Where(d => d.VendorID == vendorId ||
                            (nameHint.Length > 0 && HidCatalog.ProductName(d).Contains(nameHint, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("[HID TANI] HID listesi alınamadı", ex);
            return;
        }

        sb.AppendLine($"Üretici {vendorId:X4} ('{nameHint}') HID arayüzü sayısı: {devices.Count}");
        var readable = new List<HidDevice>();

        foreach (var d in devices)
        {
            var usages = HidCatalog.Usages(d);
            sb.AppendLine($"- PID {d.ProductID:X4} '{HidCatalog.ProductName(d)}'");
            sb.AppendLine($"  yol: {d.DevicePath}");
            sb.AppendLine($"  üst kullanımlar: {string.Join(", ", usages.Select(u => $"{u >> 16:X4}:{u & 0xFFFF:X4}"))}");
            sb.AppendLine($"  rapor boyları (giriş/çıkış/özellik): {Safe(d.GetMaxInputReportLength)}/{Safe(d.GetMaxOutputReportLength)}/{Safe(d.GetMaxFeatureReportLength)}");

            try
            {
                foreach (var report in d.GetReportDescriptor().Reports)
                {
                    var all = report.DataItems.SelectMany(i => i.Usages.GetAllValues()).ToList();
                    if (all.Contains(UsageBatteryStrength) || all.Contains(UsageRelativeStateOfCharge) || all.Contains(UsageAbsoluteStateOfCharge))
                        sb.AppendLine($"  *** STANDART PİL ALANI VAR: {report.ReportType} raporu, kimlik {report.ReportID}");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  rapor tanımlayıcısı okunamadı: {ex.Message}");
            }

            // Klavye/fare koleksiyonlarını Windows kilitler; yalnızca diğerlerini dinleyebiliriz
            var isInput = usages.Contains(HidCatalog.UsageKeyboard) || usages.Contains(HidCatalog.UsageMouse);
            if (!isInput && Safe(d.GetMaxInputReportLength) > 0) readable.Add(d);
        }

        var monitor = KeychronBatteryMonitor.Shared.Latest;
        sb.AppendLine(monitor is null
            ? "0xA4 pil raporu (topluluk firmware'i): son 5 dakikada GELMEDİ"
            : $"0xA4 pil raporu: %{monitor.Percent}, {monitor.VoltageMv} mV, bağlantı {monitor.TransportText}");

        foreach (var name in new[] { nameHint }.Where(n => n.Length > 0))
        {
            var bt = await BluetoothBattery.TryGetAsync(name);
            sb.AppendLine($"Bluetooth pil ('{name}'): {(bt is null ? "yok" : $"%{bt}")}");
        }

        sb.AppendLine($"{listenFor.TotalSeconds:0} sn boyunca {readable.Count} arayüz dinleniyor (bu sırada klavyede birkaç tuşa bas)…");
        Log.Info(sb.ToString());

        var lines = new List<string>();
        var seen = new HashSet<string>();
        var until = DateTime.UtcNow + listenFor;
        var tasks = readable.Select(d => Task.Run(() => Listen(d, until, lines, seen))).ToList();
        await Task.WhenAll(tasks);

        var result = new StringBuilder();
        result.AppendLine($"Dinleme bitti: {lines.Count} farklı rapor");
        foreach (var line in lines.Take(80)) result.AppendLine("  " + line);
        result.AppendLine("========== [HID TANI] bitti ==========");
        Log.Info(result.ToString());
    });

    private static void Listen(HidDevice device, DateTime until, List<string> lines, HashSet<string> seen)
    {
        try
        {
            if (!device.TryOpen(out HidStream stream))
            {
                lock (lines) lines.Add($"PID {device.ProductID:X4}: açılamadı");
                return;
            }

            using (stream)
            {
                stream.ReadTimeout = 1000;
                var buffer = new byte[Math.Max(device.GetMaxInputReportLength(), 1)];
                while (DateTime.UtcNow < until)
                {
                    try
                    {
                        var n = stream.Read(buffer, 0, buffer.Length);
                        var hex = Convert.ToHexString(buffer, 0, Math.Min(n, 33));
                        lock (lines)
                        {
                            if (seen.Add(hex) && lines.Count < 200)
                                lines.Add($"{DateTime.Now:HH:mm:ss} PID {device.ProductID:X4} [{n}] {hex}");
                        }
                    }
                    catch (TimeoutException)
                    {
                        // veri yok
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lock (lines) lines.Add($"PID {device.ProductID:X4}: hata {ex.Message}");
        }
    }

    private static int Safe(Func<int> f)
    {
        try
        {
            return f();
        }
        catch
        {
            return -1;
        }
    }
}
