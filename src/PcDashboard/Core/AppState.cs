using System.Text.Json;

namespace PcDashboard.Core;

/// <summary>Kartta yapılan seçimler gibi, uygulamanın kendisinin değiştirdiği durumu saklar.</summary>
public sealed class AppState
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static AppState? _current;

    public static string FilePath { get; } = Path.Combine(AppPaths.DataDirectory, "state.json");

    /// <summary>Yuva adı (keyboard, mouse, headphones) → kullanıcının seçtiği aygıt.</summary>
    public Dictionary<string, DeviceChoice> DeviceChoices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Otomatik bulunan son konum (hava durumu / saat kartı).</summary>
    public SavedLocation? Location { get; set; }

    /// <summary>Pencere modu: "fullscreen" | "maximized" | "windowed".</summary>
    public string? WindowMode { get; set; }

    /// <summary>Pencere modundaki son konum ve boyut (fiziksel piksel): x, y, genişlik, yükseklik.</summary>
    public int[]? WindowBounds { get; set; }

    /// <summary>Sağ tık menüsündeki "Windows ile başlat" seçimi (null: hiç seçilmedi).</summary>
    public bool? StartWithWindows { get; set; }

    /// <summary>Uygulama başlatıcısındaki uygulamalar (null: henüz değiştirilmedi, ayarlardaki varsayılanlar kullanılır).</summary>
    public List<AppEntry>? Apps { get; set; }

    /// <summary>Gece Işığı kaydının, uygulama ilk kez değiştirmeden önceki hali (Base64).</summary>
    public string? NightLightBackup { get; set; }

    public static AppState Current
    {
        get
        {
            lock (Gate)
            {
                return _current ??= Load();
            }
        }
    }

    private static AppState Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath));
                if (state is not null)
                {
                    state.DeviceChoices = new Dictionary<string, DeviceChoice>(state.DeviceChoices, StringComparer.OrdinalIgnoreCase);
                    return state;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("state.json okunamadı", ex);
        }

        return new AppState();
    }

    public void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            }
            catch (Exception ex)
            {
                Log.Error("state.json yazılamadı", ex);
            }
        }
    }
}

public sealed class SavedLocation
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string City { get; set; } = "";
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>Seçilen bir aygıt. HID için VendorId/ProductId, ses için Id (Windows uç nokta kimliği) kullanılır.</summary>
public sealed class DeviceChoice
{
    public string Name { get; set; } = "";
    public int? VendorId { get; set; }
    public int? ProductId { get; set; }
    public string? AudioId { get; set; }
}
