namespace PcDashboard.Core;

public sealed class AppSettings
{
    public string UserName { get; set; } = "";
    public string Accent { get; set; } = "#8AB4F8";
    public string CardTint { get; set; } = "#161822";
    public double CardOpacity { get; set; } = 0.30;
    public bool StartWithWindows { get; set; }

    public DisplaySettings Display { get; set; } = new();
    public WallpaperSettings Wallpaper { get; set; } = new();
    public ClockSettings Clock { get; set; } = new();
    public WeatherSettings Weather { get; set; } = new();
    public CalendarSettings Calendar { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
    public NewsSettings News { get; set; } = new();
    public DevicesSettings Devices { get; set; } = new();
    public ControlsSettings Controls { get; set; } = new();
    public NetworkSettings Network { get; set; } = new();
    public SystemMonitorSettings SystemMonitor { get; set; } = new();
    public AppsSettings Apps { get; set; } = new();
    public DesktopSettings Desktop { get; set; } = new();
    public StandbySettings Standby { get; set; } = new();
}

/// <summary>
/// Bekleme ekranı (ekran koruyucu) ve kilitleme. Windows'un kilit / parola ekranının içine uygulamalar çizemez;
/// bu yüzden WinDash kilitlemeden önce kendi tam ekran görünümünü gösterir, dokunulunca Windows'u kilitler.
/// </summary>
public sealed class StandbySettings
{
    /// <summary>
    /// Bu kadar dakika fare / klavye kullanılmazsa bekleme ekranı açılır (ekran koruyucu gibi) ve çıkışta
    /// <see cref="LockOnResume"/>'a göre kilitlenir. 0: kapalı (varsayılan; boşta WinDash açılmaz, kilitlenmez).
    /// </summary>
    public int IdleMinutes { get; set; }

    /// <summary>Bekleme ekranından çıkarken Windows kilitlensin (parola / Oturum aç ekranı gelsin).</summary>
    public bool LockOnResume { get; set; } = true;

    /// <summary>Bekleme ekranını hemen açıp kilitleyen genel kısayol ("WinDash ile kilitle"). Boş: yok.</summary>
    public string LockHotkey { get; set; } = "Win+Shift+L";

    /// <summary>Oturum açılınca / kilit açılınca WinDash masaüstüne gömülü moda geçsin.</summary>
    public bool DesktopModeAfterSignIn { get; set; } = true;
}

/// <summary>
/// "Masaüstüne göm" modu: WinDash penceresi gizlenir, seçilen kartlar masaüstünün sağ kenarında
/// (pencerelerin arkasında, duvar kâğıdının üzerinde) sade cam kartlar olarak, ekranın tam yüksekliğinde durur.
/// </summary>
public sealed class DesktopSettings
{
    /// <summary>Panelin duracağı monitör (ör. \\.\DISPLAY1). Boşsa ana ekran.</summary>
    public string MonitorDeviceName { get; set; } = "";

    /// <summary>Panel genişliği: monitör genişliğinin yüzdesi (çözünürlükten bağımsız). Panel her zaman tam yüksekliktedir.</summary>
    public double WidthPercent { get; set; } = 20;

    /// <summary>
    /// Panel ızgarası: sütun sayısı ve bir satırın yüksekliği (tasarım birimi; panel genişliği 400 birimdir).
    /// Satır sayısı ekran yüksekliğine göre hesaplanır. Kartlar Düzenle modunda taşınıp boyutlandırılabilir.
    /// </summary>
    public int Columns { get; set; } = 4;
    public double RowHeight { get; set; } = 20;

    /// <summary>Kartın altındaki camın bulanıklığı (CSS blur px, 1080 yüksekliğe göre). Renkler değişmez.</summary>
    public double BlurRadius { get; set; } = 30;

    /// <summary>Kartın camın üstündeki ince rengi (#AARRGGBB). "#00000000": yalnızca bulanık duvar kâğıdı.</summary>
    public string CardTint { get; set; } = "#1F000000";

    /// <summary>
    /// Varsayılan: tek sütun; saat · hava durumu · şu an çalıyor · ağ, Bluetooth, parlaklık, ses · cihazlar · sistem.
    /// Kullanıcı Düzenle modunda değiştirirse yerleşim state.json'a (DesktopLayout) kaydedilir ve bu liste kullanılmaz.
    /// "compact" sade masaüstü görünümüdür.
    /// </summary>
    public List<WidgetPlacement> Widgets { get; set; } = new()
    {
        new WidgetPlacement { Type = "clock", Column = 0, Row = 0, ColumnSpan = 4, RowSpan = 8, Variant = "compact" },
        new WidgetPlacement { Type = "weather", Column = 0, Row = 8, ColumnSpan = 4, RowSpan = 8, Variant = "compact" },
        new WidgetPlacement { Type = "media", Column = 0, Row = 16, ColumnSpan = 4, RowSpan = 8, Variant = "compact" },
        new WidgetPlacement { Type = "controls", Column = 0, Row = 24, ColumnSpan = 4, RowSpan = 12, Variant = "compact" },
        new WidgetPlacement { Type = "devices", Column = 0, Row = 36, ColumnSpan = 4, RowSpan = 10, Variant = "compact" },
        new WidgetPlacement { Type = "system", Column = 0, Row = 46, ColumnSpan = 4, RowSpan = 6, Variant = "compact" },
    };
}

public sealed class DisplaySettings
{
    public string MonitorDeviceName { get; set; } = "";
    public bool PreferSecondary { get; set; } = true;
    public int PreferredWidth { get; set; } = 1920;
    public int PreferredHeight { get; set; } = 1080;
    public bool Topmost { get; set; }
}

public sealed class WallpaperSettings
{
    public double BlurRadius { get; set; } = 60;
    public double DimOpacity { get; set; } = 0.22;
    public string OverridePath { get; set; } = "";
}

public sealed class ClockSettings
{
    /// <summary>Saatin yanında yazan yer adı. Boşsa bulunan konumun şehri.</summary>
    public string LocationLabel { get; set; } = "";
    public bool Use24Hour { get; set; } = true;
    public bool ShowSeconds { get; set; } = true;
}

public sealed class WeatherSettings
{
    /// <summary>Boşsa konum otomatik bulunur (Windows konum hizmeti, yoksa IP adresi).</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
    public int RefreshMinutes { get; set; } = 15;
}

public sealed class CalendarSettings
{
    public List<string> IcsUrls { get; set; } = new();
    public int RefreshMinutes { get; set; } = 15;
}

public sealed class MediaSettings
{
    public string PreferredApp { get; set; } = "Spotify";
    public bool FallbackToAnyPlayer { get; set; } = true;
}

public sealed class NewsSettings
{
    public int RefreshMinutes { get; set; } = 10;
    public int MaxItems { get; set; } = 4;
    public int RotateSeconds { get; set; }
    public List<FeedConfig> Feeds { get; set; } = new();
}

public sealed class FeedConfig
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Category { get; set; } = "Gündem";
}

public sealed class DevicesSettings
{
    /// <summary>Kartta görünecek yuvalar, sırasıyla: keyboard, mouse, headphones, mic, output.</summary>
    public List<DeviceSlotConfig> Slots { get; set; } = new();

    /// <summary>Windows ses aygıtı adlarını daha okunur adlara çevirir (ör. bir ses kartı çıkışına bağlı hoparlörün markası).</summary>
    public List<AudioAlias> AudioAliases { get; set; } = new();

    /// <summary>Keychron klavyelerin pil yayınını (topluluk firmware'i, raw HID 0xA4) dinle.</summary>
    public bool KeychronBatteryListener { get; set; } = true;
}

public sealed class DeviceSlotConfig
{
    /// <summary>keyboard | mouse | headphones | mic | output</summary>
    public string Slot { get; set; } = "";

    /// <summary>Otomatik seçimde gösterilecek ad. Boşsa aygıtın kendi adı kullanılır.</summary>
    public string Name { get; set; } = "";

    public string Subtitle { get; set; } = "";

    // Otomatik eşleştirme kuralları (kullanıcı kartta başka bir aygıt seçerse o geçerli olur)
    public string? VendorId { get; set; }
    public List<string> ProductIds { get; set; } = new();
    public string? ProductNameContains { get; set; }
    public List<string> AudioNameContains { get; set; } = new();
}

public sealed class AudioAlias
{
    public string Contains { get; set; } = "";
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
}

public sealed class ControlsSettings
{
    /// <summary>Parlaklığı ayarlanacak monitörler: "all" (DDC/CI destekleyen hepsi), "primary" veya \\.\DISPLAY1 gibi bir ad.</summary>
    public string BrightnessTarget { get; set; } = "all";

    /// <summary>
    /// Gece Işığı nasıl açılıp kapatılsın:
    ///  "registry" – Windows Gece Işığı (kayıt defteri; biçim tanınmazsa dokunmaz, Ayarlar'ı açar)
    ///  "settings" – yalnızca Windows Ayarlar > Gece ışığı sayfasını açar
    /// </summary>
    public string NightLightMode { get; set; } = "registry";
}

public sealed class NetworkSettings
{
    public string PingHost { get; set; } = "1.1.1.1";
}

public sealed class SystemMonitorSettings
{
    /// <summary>Boşsa Windows'un kurulu olduğu disk.</summary>
    public List<string> Drives { get; set; } = new();
}

public sealed class AppsSettings
{
    /// <summary>Bir satırdaki kutucuk sayısı.</summary>
    public int Columns { get; set; } = 6;

    /// <summary>
    /// İlk açılışta (veya kartta "Varsayılanlara dön" seçilince) gösterilecek uygulamalar.
    /// Kartta yapılan ekleme / çıkarma / sıralama %LOCALAPPDATA%\WinDash\state.json'a kaydedilir.
    /// </summary>
    public List<AppEntry> Defaults { get; set; } = new();
}

/// <summary>
/// Başlatıcıdaki bir uygulama. Path doluysa o kullanılır (.exe, .lnk, .url veya shell:AppsFolder\...);
/// boşsa Başlat menüsü ve masaüstündeki kısayollar arasında Find listesindeki adlar sırayla aranır.
/// </summary>
public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public List<string> Find { get; set; } = new();
    public string? Arguments { get; set; }

    /// <summary>true: bilgisayarda bulunamazsa kartta hiç gösterme (varsayılan listedeki yaygın uygulamalar için).</summary>
    public bool Optional { get; set; }
}

public sealed class LayoutConfig
{
    /// <summary>Varsayılan yerleşim değiştiğinde artar; kullanıcı kopyası daha eskiyse yenisiyle değiştirilir.</summary>
    public int Version { get; set; }

    public int Columns { get; set; } = 12;
    public int Rows { get; set; } = 6;
    public List<WidgetPlacement> Widgets { get; set; } = new();
}

public sealed class WidgetPlacement
{
    public string Type { get; set; } = "";
    public int Column { get; set; }
    public int Row { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public int RowSpan { get; set; } = 1;

    /// <summary>İsteğe bağlı görünüm çeşidi (ör. system için "compact").</summary>
    public string? Variant { get; set; }
}
