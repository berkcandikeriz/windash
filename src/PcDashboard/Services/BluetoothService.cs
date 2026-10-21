using PcDashboard.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace PcDashboard.Services;

public sealed record BluetoothStatus(bool Available, bool IsOn, IReadOnlyList<string> ConnectedDevices);

/// <summary>
/// Bluetooth adaptörünün açık/kapalı durumu, açıp kapatma ve bağlı cihazlar (Windows.Devices.Radios).
/// Ayarlar > Bluetooth'taki anahtarla aynı işi yapar.
/// </summary>
public sealed class BluetoothService
{
    private Radio? _radio;
    private bool _accessRequested;

    private async Task<Radio?> GetRadioAsync()
    {
        if (_radio is not null) return _radio;

        if (!_accessRequested)
        {
            _accessRequested = true;
            try
            {
                var access = await Radio.RequestAccessAsync();
                if (access != RadioAccessStatus.Allowed) Log.Warn($"Bluetooth radyo erişimi: {access}");
            }
            catch (Exception ex)
            {
                Log.Warn($"Bluetooth radyo erişimi istenemedi: {ex.Message}");
            }
        }

        var radios = await Radio.GetRadiosAsync();
        _radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        return _radio;
    }

    public async Task<BluetoothStatus> GetStatusAsync()
    {
        try
        {
            var radio = await GetRadioAsync();
            if (radio is null) return new BluetoothStatus(false, false, Array.Empty<string>());
            if (radio.State != RadioState.On) return new BluetoothStatus(true, false, Array.Empty<string>());

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectors = new[]
            {
                BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
                BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            };

            foreach (var selector in selectors)
            {
                var found = await DeviceInformation.FindAllAsync(selector);
                foreach (var device in found)
                {
                    if (!string.IsNullOrWhiteSpace(device.Name)) names.Add(device.Name.Trim());
                }
            }

            return new BluetoothStatus(true, true, names.OrderBy(n => n).ToList());
        }
        catch (Exception ex)
        {
            Log.Warn($"Bluetooth durumu okunamadı: {ex.Message}");
            _radio = null; // adaptör yeniden takılmış olabilir; bir dahaki sefere yeniden bul
            return new BluetoothStatus(false, false, Array.Empty<string>());
        }
    }

    public async Task<bool> SetAsync(bool on)
    {
        try
        {
            var radio = await GetRadioAsync();
            if (radio is null) return false;

            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            if (result != RadioAccessStatus.Allowed)
            {
                Log.Warn($"Bluetooth {(on ? "açılamadı" : "kapatılamadı")}: {result}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth durumu değiştirilemedi", ex);
            _radio = null;
            return false;
        }
    }
}
