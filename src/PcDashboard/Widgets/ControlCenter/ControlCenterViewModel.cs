using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using PcDashboard.Core;
using PcDashboard.Services;
using PcDashboard.Widgets.Network;

namespace PcDashboard.Widgets.ControlCenter;

/// <summary>
/// Taslaktaki "Kontrol merkezi": Wi-Fi bilgisi, Bluetooth ve Gece Işığı kutucukları,
/// ekran parlaklığı ve ses seviyesi kaydırıcıları.
/// </summary>
public sealed partial class ControlCenterViewModel : WidgetViewModel
{
    private static readonly TimeSpan UserGrace = TimeSpan.FromSeconds(3);

    private readonly BluetoothService _bluetooth = new();
    private readonly NightLightService _nightLight;
    private readonly BrightnessService _brightnessService;
    private MMDevice? _output;
    private string? _outputId;
    private bool _syncing;
    private DateTime _lastBrightnessByUser;
    private DateTime _lastVolumeByUser;

    public ControlCenterViewModel(AppSettings settings)
    {
        Network = new NetworkViewModel(settings.Network);
        _nightLight = new NightLightService(settings.Controls.NightLightMode);
        _brightnessService = new BrightnessService(settings.Controls.BrightnessTarget);
        _volumeIcon = ResourceGeometry("IconVolume");
    }

    public NetworkViewModel Network { get; }

    // ---------- Bluetooth ----------
    [ObservableProperty] private bool _bluetoothOn;
    [ObservableProperty] private bool _bluetoothAvailable = true;
    [ObservableProperty] private string _bluetoothText = "…";
    [ObservableProperty] private string _bluetoothTooltip = "Bluetooth'u aç / kapat";

    // ---------- Gece Işığı ----------
    [ObservableProperty] private bool _nightLightOn;
    [ObservableProperty] private string _nightLightText = "…";

    // ---------- Parlaklık ----------
    [ObservableProperty] private double _brightness;
    [ObservableProperty] private string _brightnessText = "…";
    [ObservableProperty] private bool _brightnessSupported;

    // ---------- Ses ----------
    [ObservableProperty] private double _volume;
    [ObservableProperty] private string _volumeText = "…";
    [ObservableProperty] private bool _volumeAvailable;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private Geometry _volumeIcon;

    public override async Task StartAsync()
    {
        await Network.StartAsync();

        EveryAsync(TimeSpan.FromSeconds(5), RefreshBluetoothAsync);
        Every(TimeSpan.FromSeconds(3), RefreshNightLight);
        Every(TimeSpan.FromSeconds(1), RefreshVolume);

        var initial = await _brightnessService.InitializeAsync();
        BrightnessSupported = _brightnessService.IsSupported;
        if (initial is { } b) SetSilently(() => Brightness = b);
        BrightnessText = BrightnessSupported ? $"%{Brightness:0}" : "DDC/CI yok";
        EveryAsync(TimeSpan.FromSeconds(30), RefreshBrightnessAsync, runNow: false);
    }

    private void SetSilently(Action action)
    {
        _syncing = true;
        try
        {
            action();
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---------- Bluetooth ----------

    private async Task RefreshBluetoothAsync()
    {
        var status = await _bluetooth.GetStatusAsync();
        BluetoothAvailable = status.Available;
        BluetoothOn = status.IsOn;

        if (!status.Available)
        {
            BluetoothText = "Bulunamadı";
            BluetoothTooltip = "Bluetooth adaptörü bulunamadı";
            return;
        }

        if (!status.IsOn)
        {
            BluetoothText = "Kapalı";
            BluetoothTooltip = "Açmak için tıkla";
            return;
        }

        var count = status.ConnectedDevices.Count;
        BluetoothText = count == 0 ? "Açık · bağlı cihaz yok" : $"Açık · {count} cihaz";
        BluetoothTooltip = count == 0
            ? "Kapatmak için tıkla"
            : "Bağlı: " + string.Join(", ", status.ConnectedDevices) + "\nKapatmak için tıkla";
    }

    [RelayCommand]
    private async Task ToggleBluetoothAsync()
    {
        if (!BluetoothAvailable) return;
        BluetoothText = BluetoothOn ? "Kapatılıyor…" : "Açılıyor…";
        await _bluetooth.SetAsync(!BluetoothOn);
        await RefreshBluetoothAsync();
    }

    // ---------- Gece Işığı ----------

    private void RefreshNightLight()
    {
        var state = _nightLight.IsEnabled();
        NightLightOn = state == true;
        NightLightText = state switch
        {
            true => "Açık",
            false => "Kapalı",
            null => "Ayarlar'ı aç",
        };
    }

    [RelayCommand]
    private void ToggleNightLight()
    {
        var result = _nightLight.Toggle();
        if (result is { } on)
        {
            NightLightOn = on;
            NightLightText = on ? "Açık" : "Kapalı";
        }
    }

    [RelayCommand]
    private void OpenNightLightSettings() => NightLightService.OpenSettings();

    // ---------- Parlaklık ----------

    partial void OnBrightnessChanged(double value)
    {
        if (BrightnessSupported) BrightnessText = $"%{value:0}";
        if (_syncing || !BrightnessSupported) return;
        _lastBrightnessByUser = DateTime.UtcNow;
        _brightnessService.Set((int)Math.Round(value));
    }

    private async Task RefreshBrightnessAsync()
    {
        if (!BrightnessSupported || DateTime.UtcNow - _lastBrightnessByUser < UserGrace) return;
        var value = await _brightnessService.ReadAsync();
        if (value is { } v && DateTime.UtcNow - _lastBrightnessByUser >= UserGrace)
            SetSilently(() => Brightness = v);
    }

    // ---------- Ses (Windows varsayılan çıkışı) ----------

    private void RefreshVolume()
    {
        var id = AudioHub.DefaultId(DataFlow.Render);
        if (id != _outputId)
        {
            _output?.Dispose();
            _output = id is null ? null : AudioHub.GetById(id);
            _outputId = id;
        }

        VolumeAvailable = _output is not null;
        if (_output is null)
        {
            VolumeText = "Çıkış yok";
            return;
        }

        try
        {
            var endpoint = _output.AudioEndpointVolume;
            IsMuted = endpoint.Mute;
            VolumeIcon = ResourceGeometry(IsMuted ? "IconVolumeMute" : "IconVolume");

            if (DateTime.UtcNow - _lastVolumeByUser >= TimeSpan.FromSeconds(1))
                SetSilently(() => Volume = Math.Round(endpoint.MasterVolumeLevelScalar * 100));

            VolumeText = IsMuted ? "Sessiz" : $"%{Volume:0}";
        }
        catch (Exception ex)
        {
            Log.Warn($"Ses seviyesi okunamadı: {ex.Message}");
            _output.Dispose();
            _output = null;
            _outputId = null;
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (!IsMuted) VolumeText = $"%{value:0}";
        if (_syncing || _output is null) return;

        _lastVolumeByUser = DateTime.UtcNow;
        try
        {
            _output.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100.0, 0, 1);
            if (IsMuted && value > 0) _output.AudioEndpointVolume.Mute = false;
        }
        catch (Exception ex)
        {
            Log.Warn($"Ses seviyesi ayarlanamadı: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ToggleMute()
    {
        if (_output is null) return;
        try
        {
            _output.AudioEndpointVolume.Mute = !_output.AudioEndpointVolume.Mute;
        }
        catch (Exception ex)
        {
            Log.Warn($"Sessize alınamadı: {ex.Message}");
        }

        RefreshVolume();
    }

    protected override void OnDisposing()
    {
        Network.Dispose();
        _brightnessService.Dispose();
        _output?.Dispose();
        _output = null;
    }
}
