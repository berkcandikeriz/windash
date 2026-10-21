using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PcDashboard.Core;

namespace PcDashboard.Widgets.Network;

public sealed partial class NetworkViewModel : WidgetViewModel
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly NetworkSettings _settings;
    private readonly Ping _ping = new();
    private string? _interfaceId;
    private long _lastRx;
    private long _lastTx;
    private DateTime _lastSample;

    [ObservableProperty] private string _title = "Bağlantı aranıyor…";
    [ObservableProperty] private string _details = "";
    [ObservableProperty] private string _latency = "—";
    [ObservableProperty] private string _throughput = "";
    [ObservableProperty] private Geometry _icon;

    public NetworkViewModel(NetworkSettings settings)
    {
        _settings = settings;
        _icon = ResourceGeometry("IconWifi");
    }

    public override Task StartAsync()
    {
        EveryAsync(TimeSpan.FromSeconds(10), RefreshConnectionAsync);
        EveryAsync(TimeSpan.FromSeconds(5), PingAsync);
        Every(TimeSpan.FromSeconds(2), SampleThroughput);
        return Task.CompletedTask;
    }

    // ---------- Bağlantı bilgisi ----------

    private async Task RefreshConnectionAsync()
    {
        var nic = ActiveInterface();
        _interfaceId = nic?.Id;

        if (nic is null)
        {
            Title = "Bağlantı yok";
            Details = "Ağ bağdaştırıcısı bulunamadı";
            Icon = ResourceGeometry("IconWifiOff");
            return;
        }

        var ip = nic.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
        {
            var wifi = await WifiInfo.QueryAsync();
            Icon = ResourceGeometry("IconWifi");

            if (wifi.Ssid is null)
            {
                Title = wifi.NeedsLocation ? "Wi-Fi (ad gizli)" : "Wi-Fi";
                Details = wifi.NeedsLocation
                    ? "Ağ adı için Windows konum izni gerekli"
                    : Join(ip);
                return;
            }

            Title = wifi.Ssid;
            Details = Join(wifi.Band, wifi.Signal is { } s ? $"Sinyal %{s}" : null, ip);
        }
        else
        {
            Icon = ResourceGeometry("IconEthernet");
            Title = "Ethernet";
            var speed = nic.Speed > 0 ? $"{nic.Speed / 1_000_000} Mb/s" : null;
            Details = Join("Kablolu", speed, ip);
        }
    }

    private static NetworkInterface? ActiveInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Where(n => n.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error("Ağ bağdaştırıcıları okunamadı", ex);
            return null;
        }
    }

    // ---------- Gecikme ----------

    private async Task PingAsync()
    {
        try
        {
            var reply = await _ping.SendPingAsync(_settings.PingHost, 1500);
            Latency = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "zaman aşımı";
        }
        catch
        {
            Latency = "—";
        }
    }

    // ---------- Anlık hız ----------

    private void SampleThroughput()
    {
        if (_interfaceId is null)
        {
            Throughput = "";
            return;
        }

        var nic = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Id == _interfaceId);
        if (nic is null) return;

        var stats = nic.GetIPStatistics();
        var now = DateTime.UtcNow;
        var seconds = (now - _lastSample).TotalSeconds;

        if (_lastSample != default && seconds > 0 && stats.BytesReceived >= _lastRx)
        {
            var down = (stats.BytesReceived - _lastRx) * 8 / seconds / 1_000_000;
            var up = (stats.BytesSent - _lastTx) * 8 / seconds / 1_000_000;
            Throughput = $"↓ {FormatRate(down)}  ↑ {FormatRate(up)}";
        }

        _lastRx = stats.BytesReceived;
        _lastTx = stats.BytesSent;
        _lastSample = now;
    }

    private static string FormatRate(double mbps) =>
        mbps >= 10 ? mbps.ToString("0", Tr) : mbps.ToString("0.0", Tr);

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    protected override void OnDisposing() => _ping.Dispose();
}

/// <summary>
/// Bağlı Wi-Fi ağının adı, sinyal gücü ve bandı. "netsh wlan show interfaces" çıktısı
/// dil bağımsız şekilde (değer kalıplarına bakarak) çözülür.
/// </summary>
public sealed record WifiInfo(string? Ssid, int? Signal, string? Band, bool NeedsLocation)
{
    public static async Task<WifiInfo> QueryAsync()
    {
        string output;
        try
        {
            var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };

            using var process = Process.Start(psi);
            if (process is null) return new WifiInfo(null, null, null, false);
            output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Log.Error("netsh çalıştırılamadı", ex);
            return new WifiInfo(null, null, null, false);
        }

        var ssid = Regex.Match(output, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
        var signal = Regex.Match(output, @":\s*(\d{1,3})\s*%");
        var band = Regex.Match(output, @":\s*(\d(?:[.,]\d)?)\s*GHz", RegexOptions.IgnoreCase);
        var channel = Regex.Match(output, @"^\s*(?:Channel|Kanal)\s*:\s*(\d+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        string? bandText = null;
        if (band.Success)
        {
            bandText = $"{band.Groups[1].Value.Replace('.', ',')} GHz";
        }
        else if (channel.Success && int.TryParse(channel.Groups[1].Value, out var ch))
        {
            bandText = ch <= 14 ? "2,4 GHz" : "5 GHz";
        }

        var needsLocation = !ssid.Success &&
                            (output.Contains("location", StringComparison.OrdinalIgnoreCase) ||
                             output.Contains("konum", StringComparison.OrdinalIgnoreCase));

        return new WifiInfo(
            ssid.Success ? ssid.Groups[1].Value : null,
            signal.Success && int.TryParse(signal.Groups[1].Value, out var s) ? s : null,
            bandText,
            needsLocation);
    }
}
