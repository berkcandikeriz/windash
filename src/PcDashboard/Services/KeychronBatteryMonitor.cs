using HidSharp;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>Keychron klavyeden gelen pil raporu.</summary>
public sealed record KeychronBatteryReading(int Percent, int VoltageMv, int ChargingState, int Transport, int ModelId, DateTime ReceivedUtc)
{
    public bool IsCharging => ChargingState is 1 or 2;

    public string TransportText => Transport switch
    {
        0x01 => "USB",
        0x02 => "Bluetooth",
        0x04 => "2.4 GHz",
        _ => "",
    };
}

/// <summary>
/// Keychron klavyelerin pil durumunu QMK raw HID kanalından (kullanım sayfası 0xFF60, kullanım 0x61) okur.
///
/// ÖNEMLİ: Keychron'un standart K10 HE firmware'i pil bilgisini bu kanala yazmaz; yalnızca Bluetooth üzerinden
/// gönderir. 2.4 GHz alıcı üzerinden pil görmek için topluluk firmware'i (KC_GET_BATTERY = 0xA4,
/// github.com/OutersGIT/k10he-battery-firmware) gerekir. Bu firmware:
///  - kablo ile bağlıyken 0xA4 isteğine yanıt verir (çekme),
///  - 2.4 GHz'de alıcı üzerinden birkaç saniyede bir raporu kendiliğinden gönderir (itme).
/// Biz her ikisini de destekliyoruz: tüm Keychron raw HID arayüzlerini dinler, 30 sn'de bir istek göndeririz.
///
/// Rapor: [0]=0xA4, [1]=yüzde, [2..3]=mV (little-endian), [4]=0 pilde / 1 şarjda / 2 dolu, [5]=bağlantı, [6]=model.
/// </summary>
public sealed class KeychronBatteryMonitor : IDisposable
{
    public const int KeychronVendorId = 0x3434;
    private const byte CommandGetBattery = 0xA4;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public static KeychronBatteryMonitor Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, Listener> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _scanTimer;
    private Timer? _pullTimer;
    private KeychronBatteryReading? _latest;

    public event EventHandler? Updated;

    /// <summary>Son 5 dakika içinde gelmiş rapor (yoksa null).</summary>
    public KeychronBatteryReading? Latest
    {
        get
        {
            var r = _latest;
            return r is not null && DateTime.UtcNow - r.ReceivedUtc < MaxAge ? r : null;
        }
    }

    /// <summary>Raw HID arayüzü olan bir Keychron aygıtı (klavye veya alıcı) takılı mı?</summary>
    public bool HasInterface
    {
        get
        {
            lock (_gate) return _listeners.Count > 0;
        }
    }

    public void EnsureStarted()
    {
        lock (_gate)
        {
            if (_scanTimer is not null) return;
            _scanTimer = new Timer(_ => Scan(), null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
            _pullTimer = new Timer(_ => RequestAll(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30));
        }
    }

    private void Scan()
    {
        try
        {
            var devices = DeviceList.Local.GetHidDevices(KeychronVendorId)
                .Where(d => HidCatalog.Usages(d).Contains(HidCatalog.UsageQmkRawHid))
                .ToList();

            lock (_gate)
            {
                foreach (var dead in _listeners.Where(l => !l.Value.IsAlive).Select(l => l.Key).ToList())
                {
                    _listeners[dead].Dispose();
                    _listeners.Remove(dead);
                }

                foreach (var device in devices)
                {
                    if (_listeners.ContainsKey(device.DevicePath)) continue;
                    var listener = Listener.TryStart(device, OnReport);
                    if (listener is not null) _listeners[device.DevicePath] = listener;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Keychron arayüzleri taranamadı: {ex.Message}");
        }
    }

    /// <summary>Kablolu bağlantıda klavye isteğe yanıt verir; alıcı isteği iletmez ama zararı da yoktur.</summary>
    private void RequestAll()
    {
        List<Listener> listeners;
        lock (_gate) listeners = _listeners.Values.ToList();
        foreach (var l in listeners) l.Request(CommandGetBattery);
    }

    private void OnReport(byte[] buffer, int length)
    {
        // Windows'ta ilk bayt rapor kimliğidir (0); bazı sürücülerde olmayabilir
        var o = buffer[0] == CommandGetBattery ? 0 : (length > 1 && buffer[1] == CommandGetBattery ? 1 : -1);
        if (o < 0 || length < o + 6) return;

        int percent = buffer[o + 1];
        var mv = buffer[o + 2] | (buffer[o + 3] << 8);
        int charging = buffer[o + 4];
        int transport = buffer[o + 5];
        var model = length > o + 6 ? buffer[o + 6] : 0;

        // Standart firmware 0xA4'ü tanımaz; geçersiz verileri ele (Li-ion hücre ~3.0–4.4 V)
        if (percent > 100 || mv is < 2800 or > 4500 || charging > 2) return;

        _latest = new KeychronBatteryReading(percent, mv, charging, transport, model, DateTime.UtcNow);
        Updated?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _scanTimer?.Dispose();
            _pullTimer?.Dispose();
            _scanTimer = null;
            _pullTimer = null;
            foreach (var l in _listeners.Values) l.Dispose();
            _listeners.Clear();
        }
    }

    /// <summary>Tek bir raw HID arayüzünü arka plan iş parçacığında dinler.</summary>
    private sealed class Listener : IDisposable
    {
        private readonly HidDevice _device;
        private readonly HidStream _stream;
        private readonly Action<byte[], int> _onReport;
        private readonly Thread _thread;
        private volatile bool _stopped;

        private Listener(HidDevice device, HidStream stream, Action<byte[], int> onReport)
        {
            _device = device;
            _stream = stream;
            _onReport = onReport;
            _thread = new Thread(Run) { IsBackground = true, Name = "Keychron raw HID" };
        }

        public bool IsAlive => !_stopped;

        public static Listener? TryStart(HidDevice device, Action<byte[], int> onReport)
        {
            try
            {
                if (!device.TryOpen(out HidStream stream)) return null;
                stream.ReadTimeout = 3000;
                stream.WriteTimeout = 1000;
                var listener = new Listener(device, stream, onReport);
                listener._thread.Start();
                Log.Info($"Keychron raw HID dinleniyor: {HidCatalog.ProductName(device)} ({device.ProductID:X4})");
                return listener;
            }
            catch (Exception ex)
            {
                Log.Warn($"Keychron arayüzü açılamadı: {ex.Message}");
                return null;
            }
        }

        private void Run()
        {
            var buffer = new byte[Math.Max(_device.GetMaxInputReportLength(), 33)];
            while (!_stopped)
            {
                try
                {
                    var n = _stream.Read(buffer, 0, buffer.Length);
                    if (n > 0) _onReport(buffer, n);
                }
                catch (TimeoutException)
                {
                    // veri yok; dinlemeye devam
                }
                catch (Exception)
                {
                    _stopped = true; // aygıt çıkarıldı
                }
            }
        }

        public void Request(byte command)
        {
            if (_stopped) return;
            try
            {
                var report = new byte[Math.Max(_device.GetMaxOutputReportLength(), 33)];
                report[0] = 0x00; // rapor kimliği
                report[1] = command;
                _stream.Write(report);
            }
            catch
            {
                // alıcı yazmayı kabul etmeyebilir
            }
        }

        public void Dispose()
        {
            _stopped = true;
            try
            {
                _stream.Dispose();
            }
            catch
            {
                // yok say
            }
        }
    }
}
