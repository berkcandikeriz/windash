using HidSharp;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>
/// SteelSeries Aerox 3 Wireless pil seviyesi. Protokol açık kaynak rivalcfg projesinden alınmıştır:
/// 3 numaralı HID arayüzüne 0x92 komutu gönderilir (kablosuz modda 0x40 bayrağıyla: 0xD2). Yanıtın
/// ikinci baytında bit 7 şarj durumu, kalan bitler (seviye + 1) / 5 değeridir.
/// </summary>
public static class SteelSeriesBattery
{
    public const int VendorId = 0x1038;
    public static readonly int[] Aerox3WirelessPids = { 0x1838, 0x1878 };
    public static readonly int[] Aerox3WiredPids = { 0x183A, 0x187A };

    public static bool IsAerox3(int vid, int pid) =>
        vid == VendorId && (Aerox3WirelessPids.Contains(pid) || Aerox3WiredPids.Contains(pid));

    /// <summary>null: okunamadı (fare uykuda olabilir).</summary>
    public static (int Percent, bool Charging)? TryRead(int vid, int pid)
    {
        if (!IsAerox3(vid, pid)) return null;
        var command = (byte)(Aerox3WirelessPids.Contains(pid) ? 0xD2 : 0x92);

        try
        {
            var candidates = DeviceList.Local.GetHidDevices(vid, pid)
                .OrderByDescending(d => d.DevicePath.Contains("mi_03", StringComparison.OrdinalIgnoreCase));

            foreach (var device in candidates)
            {
                if (SafeOutputLength(device) <= 0) continue;
                var result = TryReadOne(device, command);
                if (result is not null) return result;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Aerox 3 pil okunamadı: {ex.Message}");
        }

        return null;
    }

    private static int SafeOutputLength(HidDevice device)
    {
        try
        {
            return device.GetMaxOutputReportLength();
        }
        catch
        {
            return 0;
        }
    }

    private static (int, bool)? TryReadOne(HidDevice device, byte command)
    {
        try
        {
            if (!device.TryOpen(out HidStream stream)) return null;
            using (stream)
            {
                stream.ReadTimeout = 600;
                stream.WriteTimeout = 600;

                var output = new byte[device.GetMaxOutputReportLength()];
                output[1] = command;
                stream.Write(output);

                var input = new byte[Math.Max(device.GetMaxInputReportLength(), 3)];
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var n = stream.Read(input, 0, input.Length);
                    for (var i = 0; i < n - 1; i++)
                    {
                        if (input[i] != command) continue;
                        var raw = input[i + 1];
                        var charging = (raw & 0x80) != 0;
                        var percent = Math.Clamp(((raw & 0x7F) - 1) * 5, 0, 100);
                        return (percent, charging);
                    }
                }
            }
        }
        catch (TimeoutException)
        {
            // fare uykuda
        }
        catch (Exception ex)
        {
            Log.Warn($"Aerox 3 arayüzü okunamadı: {ex.Message}");
        }

        return null;
    }
}
