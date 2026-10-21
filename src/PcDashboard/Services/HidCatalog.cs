using System.Collections.Concurrent;
using HidSharp;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>Bağlı bir klavye / fare (veya kablosuz alıcısı). Aynı VID:PID'e sahip arayüzler tek ürün sayılır.</summary>
public sealed record HidProduct(int VendorId, int ProductId, string Name, bool IsKeyboard, bool IsMouse, bool IsBluetooth)
{
    public string Key => $"{VendorId:X4}:{ProductId:X4}";

    private static readonly string[] ReceiverWords = { "receiver", "dongle", "wireless", "link", "alıcı", "2.4" };

    /// <summary>Kablosuz alıcı (dongle) gibi görünüyor mu? (Ürün adına göre tahmin.)</summary>
    public bool LooksLikeReceiver => ReceiverWords.Any(w => Name.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Windows'un genel adı mı (üretici adı bildirmemiş)?</summary>
    public bool HasGenericName =>
        Name.StartsWith("HID", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("Keyboard Device", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("USB Input Device", StringComparison.OrdinalIgnoreCase) ||
        Name.StartsWith("HID aygıtı", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bağlantı türü: "Bluetooth", "Kablosuz alıcı" veya "USB".</summary>
    public string ConnectionText => IsBluetooth ? "Bluetooth" : LooksLikeReceiver ? "Kablosuz alıcı" : "USB";
}

/// <summary>
/// Windows'a bağlı klavye ve fareleri HID üzerinden listeler. Bir arayüzün klavye mi fare mi olduğu
/// HID rapor tanımlayıcısındaki üst düzey kullanımdan (Generic Desktop 0x06 / 0x02) anlaşılır.
/// </summary>
public static class HidCatalog
{
    public const uint UsageKeyboard = 0x00010006;
    public const uint UsageMouse = 0x00010002;
    public const uint UsageQmkRawHid = 0xFF600061;

    private static readonly ConcurrentDictionary<string, uint[]> UsageCache = new(StringComparer.OrdinalIgnoreCase);

    public static List<HidProduct> Enumerate()
    {
        var products = new Dictionary<(int Vid, int Pid), (string Name, bool Kb, bool Mouse, bool Bt)>();

        IEnumerable<HidDevice> devices;
        try
        {
            devices = DeviceList.Local.GetHidDevices().ToList();
        }
        catch (Exception ex)
        {
            Log.Error("HID aygıtları listelenemedi", ex);
            return new List<HidProduct>();
        }

        foreach (var device in devices)
        {
            var usages = Usages(device);
            var kb = usages.Contains(UsageKeyboard);
            var mouse = usages.Contains(UsageMouse);

            var key = (device.VendorID, device.ProductID);
            products.TryGetValue(key, out var current);
            var name = string.IsNullOrWhiteSpace(current.Name) ? ProductName(device) : current.Name;
            // Bluetooth HID aygıtlarının yolunda BTHENUM / BTHLE geçer ({00001124-…} HID hizmeti)
            var bt = device.DevicePath.Contains("bth", StringComparison.OrdinalIgnoreCase) ||
                     device.DevicePath.Contains("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase);
            products[key] = (name, current.Kb || kb, current.Mouse || mouse, current.Bt || bt);
        }

        return products
            .Where(p => p.Value.Kb || p.Value.Mouse)
            .Select(p => new HidProduct(p.Key.Vid, p.Key.Pid,
                string.IsNullOrWhiteSpace(p.Value.Name) ? $"HID aygıtı {p.Key.Vid:X4}:{p.Key.Pid:X4}" : p.Value.Name,
                p.Value.Kb, p.Value.Mouse, p.Value.Bt))
            .ToList();
    }

    /// <summary>Arayüzün üst düzey HID kullanımları (sayfa &lt;&lt; 16 | kullanım). Önbelleğe alınır.</summary>
    public static uint[] Usages(HidDevice device) =>
        UsageCache.GetOrAdd(device.DevicePath, _ =>
        {
            try
            {
                return device.GetReportDescriptor().DeviceItems
                    .SelectMany(item => item.Usages.GetAllValues())
                    .Distinct()
                    .ToArray();
            }
            catch
            {
                return Array.Empty<uint>();
            }
        });

    public static string ProductName(HidDevice device)
    {
        try
        {
            return device.GetProductName()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
