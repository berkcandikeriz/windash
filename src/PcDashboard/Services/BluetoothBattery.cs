using System.Collections.Concurrent;
using PcDashboard.Core;
using Windows.Devices.Enumeration;

namespace PcDashboard.Services;

/// <summary>
/// Bluetooth ile bağlı cihazların Windows'un gösterdiği pil yüzdesi (Ayarlar > Bluetooth'taki değer).
/// Keychron K10 HE Bluetooth modunda pilini bu yolla bildirir.
/// </summary>
public static class BluetoothBattery
{
    // DEVPKEY_Bluetooth_Battery
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(60);
    private static readonly ConcurrentDictionary<string, (int? Value, DateTime At)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adında verilen ifade geçen ve pil bilgisi olan ilk cihazın yüzdesi.</summary>
    public static async Task<int?> TryGetAsync(string nameContains)
    {
        if (string.IsNullOrWhiteSpace(nameContains)) return null;
        var key = nameContains.Trim();
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < CacheTime) return cached.Value;

        int? result = null;
        try
        {
            var safe = key.Replace('"', ' ').Trim();
            var aqs = "System.ItemNameDisplay:~~\"" + safe + "\"";
            var devices = await DeviceInformation.FindAllAsync(aqs, new[] { BatteryKey }, DeviceInformationKind.Device);
            foreach (var device in devices)
            {
                if (device.Properties.TryGetValue(BatteryKey, out var value) && value is byte b && b <= 100)
                {
                    result = b;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Bluetooth pil bilgisi okunamadı ({key}): {ex.Message}");
        }

        Cache[key] = (result, DateTime.UtcNow);
        return result;
    }
}
