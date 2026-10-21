using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HidSharp;
using NAudio.CoreAudioApi;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Devices;

public sealed partial class MeterSegment : ObservableObject
{
    [ObservableProperty] private bool _isOn;
}

/// <summary>Seçim listesindeki bir satır.</summary>
public sealed record SlotOption(string Label, string? Detail, bool IsSelected, bool IsAuto, HidProduct? Hid, string? AudioId);

/// <summary>Kartta bir satır: klavye, mouse, kulaklık, mikrofon veya çıkış yuvası.</summary>
public sealed partial class DeviceSlotViewModel : ObservableObject
{
    private const int SegmentCount = 20;

    public DeviceSlotViewModel(DeviceSlotConfig config, string kind, string title, Geometry icon, string quickTip)
    {
        Config = config;
        Kind = kind;
        Title = title;
        Icon = icon;
        QuickActionTooltip = quickTip;
        _name = string.IsNullOrWhiteSpace(config.Name) ? title : config.Name;
        _subtitle = config.Subtitle;
        for (var i = 0; i < SegmentCount; i++) Meter.Add(new MeterSegment());
    }

    public DeviceSlotConfig Config { get; }

    /// <summary>keyboard | mouse | headphones | mic | output</summary>
    public string Kind { get; }

    /// <summary>"Klavye", "Mouse"…</summary>
    public string Title { get; }

    public Geometry Icon { get; }
    public string QuickActionTooltip { get; }
    public ObservableCollection<MeterSegment> Meter { get; } = new();

    public bool IsHid => Kind is "keyboard" or "mouse";

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _statusText = "…";
    [ObservableProperty] private Brush _statusBrush = Brushes.White;
    [ObservableProperty] private bool _statusMono;
    [ObservableProperty] private string _subtitle;
    [ObservableProperty] private bool _showSubtitle = true;
    [ObservableProperty] private bool _showBattery;
    [ObservableProperty] private double _battery;
    [ObservableProperty] private Brush? _batteryBrush;
    [ObservableProperty] private bool _showMeter;
    [ObservableProperty] private string _tooltip = "";

    // ---- Çalışma anı durumu ----
    internal HidProduct? Hid;
    internal List<HidProduct> HidCandidates = new();
    internal string? AudioId;
    internal MMDevice? Audio;
    internal int? BatteryPercent;
    internal bool Charging;
    internal bool Sleeping;
    internal string? BatteryNote;

    internal void Show(string status, Brush statusBrush, bool mono, string subtitle)
    {
        StatusText = status;
        StatusBrush = statusBrush;
        StatusMono = mono;
        Subtitle = subtitle;
    }

    internal void SetLine2(bool battery, bool meter)
    {
        ShowBattery = battery;
        ShowMeter = meter && !battery;
        ShowSubtitle = !ShowBattery && !ShowMeter;
    }

    internal void SetLevel(float level)
    {
        // Algıya daha yakın olsun diye karekök ölçeği
        var lit = (int)Math.Round(Math.Sqrt(Math.Clamp(level, 0f, 1f)) * SegmentCount);
        for (var i = 0; i < Meter.Count; i++)
        {
            var on = i < lit;
            if (Meter[i].IsOn != on) Meter[i].IsOn = on;
        }
    }
}

/// <summary>
/// Cihazlar kartı. Her yuva ya kullanıcının kartta seçtiği aygıtı ya da (Otomatik'te) ayarlardaki kurala uyan /
/// Windows'ta seçili olan aygıtı gösterir:
///  - Klavye / Mouse: bağlı HID klavye ve fareler. Seçim state.json'a kaydedilir.
///  - Kulaklık: ses çıkış aygıtları. Satırın ikonuna tıklamak onu Windows varsayılan çıkışı yapar.
///  - Mikrofon / Çıkış: her zaman Windows varsayılanını yansıtır; listeden seçim Windows varsayılanını değiştirir.
/// </summary>
public sealed partial class DevicesViewModel : WidgetViewModel
{
    private static readonly string[] DefaultSlots = { "keyboard", "mouse", "headphones", "mic", "output" };

    private readonly DevicesSettings _settings;
    private readonly KeychronBatteryMonitor _keychron = KeychronBatteryMonitor.Shared;
    private List<HidProduct> _products = new();
    private bool _hidRefreshQueued;

    public ObservableCollection<DeviceSlotViewModel> Slots { get; } = new();

    public DevicesViewModel(DevicesSettings settings) => _settings = settings;

    public override Task StartAsync()
    {
        var configs = _settings.Slots.Count > 0
            ? _settings.Slots
            : DefaultSlots.Select(k => new DeviceSlotConfig { Slot = k }).ToList();

        foreach (var config in configs)
        {
            var kind = config.Slot.Trim().ToLowerInvariant();
            var (title, icon, tip) = kind switch
            {
                "keyboard" => ("Klavye", "IconKeyboard", "Yenile"),
                "mouse" => ("Mouse", "IconMouse", "Yenile"),
                "headphones" => ("Kulaklık", "IconHeadphones", "Varsayılan çıkış yap"),
                "mic" => ("Mikrofon", "IconMic", "Sessize al / aç"),
                "output" => ("Hoparlör", "IconSpeaker", "Sessize al / aç"),
                _ => ("", "", ""),
            };
            if (title.Length == 0)
            {
                Log.Warn($"Bilinmeyen cihaz yuvası: '{config.Slot}'");
                continue;
            }

            Slots.Add(new DeviceSlotViewModel(config, kind, title, ResourceGeometry(icon), tip));
        }

        if (_settings.KeychronBatteryListener)
        {
            _keychron.EnsureStarted();
            _keychron.Updated += OnKeychronUpdated;
        }

        try
        {
            DeviceList.Local.Changed += OnHidListChanged;
        }
        catch (Exception ex)
        {
            Log.Warn($"HID değişiklikleri izlenemiyor: {ex.Message}");
        }

        Every(TimeSpan.FromSeconds(2), RefreshAudioSlots);
        EveryAsync(TimeSpan.FromSeconds(15), RefreshHidAsync);
        EveryAsync(TimeSpan.FromSeconds(60), RefreshBatteriesAsync, runNow: false);
        Every(TimeSpan.FromMilliseconds(60), TickMeters);

        // İlk pil okuması HID listesi geldikten hemen sonra
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => OnUiAsync(() => _ = RefreshBatteriesAsync()));
        return Task.CompletedTask;
    }

    // =====================================================================
    // Klavye / Mouse (HID)
    // =====================================================================

    private void OnHidListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        if (_hidRefreshQueued) return;
        _hidRefreshQueued = true;
        // Takıp çıkarma olayları art arda gelir; biraz bekleyip tek seferde yenile
        _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => OnUiAsync(() =>
        {
            _hidRefreshQueued = false;
            _ = RefreshHidAsync();
        }));
    }

    private async Task RefreshHidAsync()
    {
        _products = await Task.Run(HidCatalog.Enumerate);

        foreach (var slot in Slots.Where(s => s.IsHid))
        {
            var other = Slots.FirstOrDefault(s => s.IsHid && s != slot)?.Config;
            slot.HidCandidates = _products
                .Where(p => slot.Kind == "keyboard" ? p.IsKeyboard : p.IsMouse)
                // Örn. fare alıcısı klavye arayüzü de sunar; başka yuvanın kuralına uyanı bu listede gösterme
                .Where(p => other is null || !MatchesHid(other, p) || MatchesHid(slot.Config, p))
                .OrderByDescending(p => MatchesHid(slot.Config, p))
                .ThenByDescending(p => KindScore(p, slot.Kind) + AutoScore(p))
                .ToList();

            var previous = slot.Hid?.Key;
            var choice = Choice(slot);
            if (choice?.VendorId is { } vid && choice.ProductId is { } pid)
                slot.Hid = slot.HidCandidates.FirstOrDefault(p => p.VendorId == vid && p.ProductId == pid);
            else
                // Kurala uyan > en olası; karşı türden olduğu kesin aygıt (klavyede fare) otomatik seçilmez
                slot.Hid = slot.HidCandidates.FirstOrDefault(p => MatchesHid(slot.Config, p) || KindScore(p, slot.Kind) > -6);

            if (slot.Hid?.Key != previous)
            {
                slot.BatteryPercent = null;
                slot.Sleeping = false;
            }

            RenderHid(slot);
        }
    }

    /// <summary>
    /// Otomatik seçimde hangi klavye / farenin gösterileceği: pilini okuyabildiğimiz aygıtlar,
    /// üretici adı olanlar, kablosuz olanlar öne çıkar; Windows'un genel adlı sanal aygıtları geride kalır.
    /// </summary>
    private static int AutoScore(HidProduct p)
    {
        var score = 0;
        if (SteelSeriesBattery.IsAerox3(p.VendorId, p.ProductId) || p.VendorId == KeychronBatteryMonitor.KeychronVendorId) score += 4;
        if (!p.HasGenericName) score += 3;
        if (p.IsBluetooth || p.LooksLikeReceiver) score += 2;
        if (p.IsKeyboard != p.IsMouse) score += 1; // yalnızca klavye ya da yalnızca fare
        return score;
    }

    private static readonly string[] KeyboardWords = { "keyboard", "klavye", "kbd", "keychron", "keys" };
    private static readonly string[] MouseWords =
        { "mouse", "fare", "aerox", "rival", "prime", "viper", "deathadder", "basilisk", "g pro", "mx master", "superlight" };

    /// <summary>
    /// Aygıtın bu yuvaya (klavye / fare) ne kadar uyduğu. Kablosuz fare alıcıları klavye arayüzü de sunar
    /// (makro tuşları için), klavye alıcıları da fare arayüzü sunabilir; bu yüzden yalnızca HID türüne bakmak yetmez.
    /// </summary>
    private static int KindScore(HidProduct p, string kind)
    {
        var keyboardSlot = kind == "keyboard";
        var knownMouse = SteelSeriesBattery.IsAerox3(p.VendorId, p.ProductId) || MouseWords.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
        var knownKeyboard = p.VendorId == KeychronBatteryMonitor.KeychronVendorId || KeyboardWords.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase));

        var score = 0;
        if (keyboardSlot)
        {
            if (knownKeyboard) score += 8;
            if (knownMouse) score -= 12;
            if (p.IsMouse && !p.IsKeyboard) score -= 6;
        }
        else
        {
            if (knownMouse) score += 8;
            if (knownKeyboard) score -= 12;
            if (p.IsKeyboard && !p.IsMouse) score -= 6;
        }

        return score;
    }

    private static bool MatchesHid(DeviceSlotConfig c, HidProduct p)
    {
        var vid = SettingsLoader.ParseHex(c.VendorId);
        var pids = c.ProductIds.Select(SettingsLoader.ParseHex).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var hasRule = vid is not null || pids.Count > 0 || !string.IsNullOrWhiteSpace(c.ProductNameContains);
        if (!hasRule) return false;
        if (vid is not null && p.VendorId != vid) return false;
        if (pids.Count > 0 && !pids.Contains(p.ProductId)) return false;
        if (!string.IsNullOrWhiteSpace(c.ProductNameContains) &&
            !p.Name.Contains(c.ProductNameContains, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void RenderHid(DeviceSlotViewModel slot)
    {
        var c = slot.Config;
        var choice = Choice(slot);

        if (slot.Hid is null)
        {
            slot.Name = choice?.Name is { Length: > 0 } n ? n : (string.IsNullOrWhiteSpace(c.Name) ? slot.Title : c.Name);
            slot.Show("Bulunamadı", ResourceBrush("MutedBrush"), false, Join(c.Subtitle, "takılı değil"));
            slot.SetLine2(battery: false, meter: false);
            slot.Tooltip = $"{slot.Title}: seçmek için tıkla";
            return;
        }

        var p = slot.Hid;
        slot.Name = MatchesHid(c, p) && !string.IsNullOrWhiteSpace(c.Name) ? c.Name : p.Name;
        var connection = MatchesHid(c, p) && !string.IsNullOrWhiteSpace(c.Subtitle) ? c.Subtitle : p.ConnectionText;
        slot.Tooltip = $"{slot.Title}: {p.Name} ({p.Key})\nBaşka bir aygıt seçmek için tıkla";

        var isKeychron = p.VendorId == KeychronBatteryMonitor.KeychronVendorId;
        if (isKeychron && _keychron.Latest is { } reading)
        {
            slot.BatteryPercent = reading.Percent;
            slot.Charging = reading.IsCharging;
            slot.BatteryNote = reading.TransportText;
        }

        if (slot.BatteryPercent is { } percent)
        {
            var low = percent <= 20 && !slot.Charging;
            slot.Show(slot.Charging ? $"%{percent} şarjda" : $"%{percent}",
                low ? ResourceBrush("WarnBrush") : ResourceBrush("TextBrush"), true, connection);
            slot.Battery = percent;
            slot.BatteryBrush = slot.Charging ? ResourceBrush("OkBrush") : low ? ResourceBrush("WarnBrush") : ResourceBrush("AccentBrush");
            slot.SetLine2(battery: true, meter: false);
            return;
        }

        if (slot.Sleeping)
        {
            slot.Show("Uykuda", ResourceBrush("MutedBrush"), false, Join(connection, "alıcı takılı"));
            slot.SetLine2(battery: false, meter: false);
            return;
        }

        var note = isKeychron
            ? (_keychron.HasInterface ? "pil raporu bekleniyor" : "pil bilgisi yok")
            : SteelSeriesBattery.IsAerox3(p.VendorId, p.ProductId) ? "pil okunuyor…" : "pil bilgisi yok";
        slot.Show("Bağlı", ResourceBrush("OkBrush"), false, Join(connection, note));
        slot.SetLine2(battery: false, meter: false);
    }

    private void OnKeychronUpdated(object? sender, EventArgs e) =>
        _ = OnUiAsync(() =>
        {
            foreach (var slot in Slots.Where(s => s.IsHid && s.Hid?.VendorId == KeychronBatteryMonitor.KeychronVendorId))
                RenderHid(slot);
        });

    // =====================================================================
    // Pil (60 sn'de bir; HID G/Ç arka planda)
    // =====================================================================

    private async Task RefreshBatteriesAsync()
    {
        foreach (var slot in Slots.ToList())
        {
            try
            {
                if (slot.IsHid && slot.Hid is { } p)
                {
                    if (SteelSeriesBattery.IsAerox3(p.VendorId, p.ProductId))
                    {
                        var r = await Task.Run(() => SteelSeriesBattery.TryRead(p.VendorId, p.ProductId));
                        slot.BatteryPercent = r?.Percent;
                        slot.Charging = r?.Charging ?? false;
                        slot.Sleeping = r is null;
                    }
                    else if (p.VendorId == KeychronBatteryMonitor.KeychronVendorId && _keychron.Latest is not null)
                    {
                        // RenderHid canlı raporu kullanır
                    }
                    else
                    {
                        // Bluetooth ile bağlıysa Windows'un bildirdiği pil
                        var bt = await BluetoothBattery.TryGetAsync(p.Name);
                        if (bt is null && !string.IsNullOrWhiteSpace(slot.Config.Name))
                            bt = await BluetoothBattery.TryGetAsync(slot.Config.Name);
                        slot.BatteryPercent = bt;
                        slot.Charging = false;
                    }

                    RenderHid(slot);
                }
                else if (slot.Kind == "headphones" && slot.Audio is not null)
                {
                    var name = AudioHub.ShortName(slot.Audio.FriendlyName);
                    slot.BatteryPercent = await BluetoothBattery.TryGetAsync(name);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Pil okunamadı ({slot.Title}): {ex.Message}");
            }
        }
    }

    // =====================================================================
    // Kulaklık / Mikrofon / Çıkış (Core Audio)
    // =====================================================================

    private void RefreshAudioSlots()
    {
        var defaultOut = AudioHub.DefaultId(DataFlow.Render);
        foreach (var slot in Slots)
        {
            try
            {
                switch (slot.Kind)
                {
                    case "headphones":
                        RenderHeadphones(slot, defaultOut);
                        break;
                    case "mic":
                        RenderDefaultEndpoint(slot, DataFlow.Capture);
                        break;
                    case "output":
                        RenderDefaultEndpoint(slot, DataFlow.Render);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Ses aygıtı okunamadı ({slot.Title}): {ex.Message}");
                Bind(slot, null);
            }
        }
    }

    /// <summary>Yuvanın tuttuğu MMDevice'ı yeni kimliğe göre günceller.</summary>
    private static void Bind(DeviceSlotViewModel slot, string? id)
    {
        if (slot.AudioId == id && slot.Audio is not null) return;
        slot.Audio?.Dispose();
        slot.Audio = id is null ? null : AudioHub.GetById(id);
        if (slot.AudioId != id) slot.BatteryPercent = null;
        slot.AudioId = slot.Audio is null ? null : id;
    }

    private void RenderHeadphones(DeviceSlotViewModel slot, string? defaultOut)
    {
        var c = slot.Config;
        var choice = Choice(slot);
        string? id = choice?.AudioId;
        if (id is null)
        {
            var endpoints = AudioHub.List(DataFlow.Render);
            var match = endpoints.FirstOrDefault(e => MatchesAudio(c, e.FriendlyName))
                        ?? endpoints.FirstOrDefault(e => e.IsHeadphones && !e.IsVirtual)
                        ?? endpoints.FirstOrDefault(e => e.IsHeadphones);
            id = match?.Id;
        }

        Bind(slot, id);

        if (slot.Audio is null)
        {
            slot.Name = choice?.Name is { Length: > 0 } n ? n : (string.IsNullOrWhiteSpace(c.Name) ? slot.Title : c.Name);
            slot.Show("Bulunamadı", ResourceBrush("MutedBrush"), false,
                "Kulaklık takılı değil · seçmek için tıkla");
            slot.SetLine2(battery: false, meter: false);
            slot.Tooltip = "Kulaklık: seçmek için tıkla";
            return;
        }

        var (name, sub) = AudioNames(c, slot.Audio.FriendlyName, AudioHub.FormFactor(slot.Audio));
        slot.Name = name;
        slot.Tooltip = $"Kulaklık: {slot.Audio.FriendlyName}\nBaşka bir aygıt seçmek için tıkla";
        var active = slot.AudioId == defaultOut;

        if (slot.BatteryPercent is { } percent)
        {
            var low = percent <= 20;
            slot.Show($"%{percent}", low ? ResourceBrush("WarnBrush") : ResourceBrush("TextBrush"), true, sub);
            slot.Battery = percent;
            slot.BatteryBrush = low ? ResourceBrush("WarnBrush") : ResourceBrush("AccentBrush");
            slot.SetLine2(battery: true, meter: false);
            return;
        }

        slot.Show(active ? "Aktif" : "Hazır", active ? ResourceBrush("OkBrush") : ResourceBrush("TextBrush"), false,
            Join(sub, active ? "varsayılan çıkış" : "pil bilgisi yok"));
        slot.SetLine2(battery: false, meter: false);
    }

    private void RenderDefaultEndpoint(DeviceSlotViewModel slot, DataFlow flow)
    {
        Bind(slot, AudioHub.DefaultId(flow));
        var c = slot.Config;

        if (slot.Audio is null)
        {
            slot.Name = string.IsNullOrWhiteSpace(c.Name) ? slot.Title : c.Name;
            slot.Show("Bulunamadı", ResourceBrush("MutedBrush"), false, "Windows'ta varsayılan aygıt yok");
            slot.SetLine2(battery: false, meter: false);
            return;
        }

        var (name, sub) = AudioNames(c, slot.Audio.FriendlyName, AudioHub.FormFactor(slot.Audio));
        slot.Name = name;
        slot.Tooltip = $"{slot.Title}: {slot.Audio.FriendlyName}\nWindows varsayılanını değiştirmek için tıkla";

        var volume = slot.Audio.AudioEndpointVolume;
        var muted = volume.Mute;

        if (flow == DataFlow.Capture)
        {
            slot.Show(muted ? "Sessizde" : "Mikrofon açık", muted ? ResourceBrush("WarnBrush") : ResourceBrush("OkBrush"), false, sub);
            slot.SetLine2(battery: false, meter: true);
        }
        else
        {
            var level = (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
            slot.Show(muted ? "Sessiz" : $"Ses %{level}", muted ? ResourceBrush("WarnBrush") : ResourceBrush("TextBrush"),
                !muted, Join(sub, "varsayılan çıkış"));
            slot.SetLine2(battery: false, meter: false);
        }
    }

    private static bool MatchesAudio(DeviceSlotConfig c, string friendlyName) =>
        c.AudioNameContains.Any(n => !string.IsNullOrWhiteSpace(n) && friendlyName.Contains(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ad: ayardaki ad (kural uyuyorsa) &gt; takma ad &gt; Windows'un bildirdiği ürün adı.
    /// Alt yazı: ayardaki / takma ad alt yazısı &gt; Windows'un bildirdiği aygıt biçimi (Kulaklık, Optik (S/PDIF)…).
    /// </summary>
    private static (string Name, string Subtitle) AudioNames(DeviceSlotConfig c, string friendlyName, int formFactor = -1)
    {
        var (aliasName, aliasSub) = AudioHub.Display(friendlyName);
        var detected = AudioHub.FormFactorText(formFactor);
        if (MatchesAudio(c, friendlyName) && !string.IsNullOrWhiteSpace(c.Name))
            return (c.Name, !string.IsNullOrWhiteSpace(c.Subtitle) ? c.Subtitle : aliasSub ?? detected);
        return (aliasName, aliasSub ?? detected);
    }

    private void TickMeters()
    {
        foreach (var slot in Slots)
        {
            if (!slot.ShowMeter) continue;
            float level;
            try
            {
                level = slot.Audio?.AudioMeterInformation.MasterPeakValue ?? 0;
            }
            catch
            {
                level = 0;
            }

            slot.SetLevel(level);
        }
    }

    // =====================================================================
    // Seçim ve hızlı işlem (görünüm çağırır)
    // =====================================================================

    public List<SlotOption> GetOptions(DeviceSlotViewModel slot)
    {
        var list = new List<SlotOption>();
        var choice = Choice(slot);

        if (slot.IsHid)
        {
            list.Add(new SlotOption("Otomatik", "Ayarlardaki kurala uyan aygıt", choice is null, true, null, null));
            foreach (var p in slot.HidCandidates)
            {
                var selected = choice?.VendorId == p.VendorId && choice.ProductId == p.ProductId;
                list.Add(new SlotOption(p.Name, p.Key, selected, false, p, null));
            }

            return list;
        }

        var flow = slot.Kind == "mic" ? DataFlow.Capture : DataFlow.Render;
        if (slot.Kind == "headphones")
            list.Add(new SlotOption("Otomatik", "Ayarlardaki kurala uyan aygıt", choice is null, true, null, null));

        var defaultId = AudioHub.DefaultId(flow);
        foreach (var e in AudioHub.List(flow)
                     .OrderBy(x => x.IsVirtual)
                     .ThenByDescending(x => slot.Kind == "headphones" && x.IsHeadphones))
        {
            var (name, kind) = AudioNames(slot.Config, e.FriendlyName, e.FormFactor);
            var selected = slot.Kind == "headphones" ? choice?.AudioId == e.Id : e.Id == defaultId;
            var detail = string.Join(" · ", new[] { kind, name == e.FriendlyName ? null : e.FriendlyName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            list.Add(new SlotOption(name, detail, selected, false, null, e.Id));
        }

        return list;
    }

    public void Select(DeviceSlotViewModel slot, SlotOption option)
    {
        var state = AppState.Current;

        if (slot.IsHid)
        {
            if (option.IsAuto || option.Hid is null) state.DeviceChoices.Remove(slot.Kind);
            else
                state.DeviceChoices[slot.Kind] = new DeviceChoice
                {
                    Name = option.Hid.Name,
                    VendorId = option.Hid.VendorId,
                    ProductId = option.Hid.ProductId,
                };
            state.Save();
            _ = RefreshHidAsync().ContinueWith(_ => OnUiAsync(() => _ = RefreshBatteriesAsync()));
            return;
        }

        if (slot.Kind == "headphones")
        {
            if (option.IsAuto || option.AudioId is null) state.DeviceChoices.Remove(slot.Kind);
            else state.DeviceChoices[slot.Kind] = new DeviceChoice { Name = option.Label, AudioId = option.AudioId };
            state.Save();
            slot.BatteryPercent = null;
        }
        else if (option.AudioId is not null)
        {
            // Mikrofon / Çıkış: Windows varsayılanını değiştir
            AudioHub.SetDefault(option.AudioId);
        }

        RefreshAudioSlots();
    }

    [RelayCommand]
    private void QuickAction(DeviceSlotViewModel? slot)
    {
        if (slot is null) return;
        try
        {
            switch (slot.Kind)
            {
                case "mic":
                case "output":
                    if (slot.Audio is not null) slot.Audio.AudioEndpointVolume.Mute = !slot.Audio.AudioEndpointVolume.Mute;
                    RefreshAudioSlots();
                    break;
                case "headphones":
                    if (slot.AudioId is not null) AudioHub.SetDefault(slot.AudioId);
                    RefreshAudioSlots();
                    break;
                default:
                    _ = RefreshHidAsync().ContinueWith(_ => OnUiAsync(() => _ = RefreshBatteriesAsync()));
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"İşlem yapılamadı ({slot.Title}): {ex.Message}");
        }
    }

    private static DeviceChoice? Choice(DeviceSlotViewModel slot) =>
        AppState.Current.DeviceChoices.TryGetValue(slot.Kind, out var c) ? c : null;

    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    protected override void OnDisposing()
    {
        _keychron.Updated -= OnKeychronUpdated;
        try
        {
            DeviceList.Local.Changed -= OnHidListChanged;
        }
        catch
        {
            // yok say
        }

        foreach (var slot in Slots)
        {
            slot.Audio?.Dispose();
            slot.Audio = null;
        }
    }
}
