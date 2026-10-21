using System.Globalization;
using System.Text.Json;

namespace PcDashboard.Core;

public static class SettingsLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Kullanıcının düzenlediği ayarların klasörü (%LOCALAPPDATA%\WinDash).</summary>
    public static string BaseDirectory => AppPaths.DataDirectory;

    public static string SettingsPath => Pick("appsettings.json");
    public static string LayoutPath => Pick("layout.json");

    public static AppSettings LoadSettings() => Load<AppSettings>(SettingsPath) ?? new AppSettings();

    public static LayoutConfig LoadLayout() => Load<LayoutConfig>(LayoutPath) ?? new LayoutConfig();

    public static int ReadLayoutVersion(string path) => Load<LayoutConfig>(path)?.Version ?? 0;

    /// <summary>Kullanıcı kopyası varsa o, yoksa kurulum klasöründeki varsayılan.</summary>
    private static string Pick(string name)
    {
        AppPaths.Prepare();
        var user = AppPaths.UserFile(name);
        return File.Exists(user) ? user : AppPaths.DefaultFile(name);
    }

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                Log.Warn($"Ayar dosyası bulunamadı: {path}");
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (Exception ex)
        {
            Log.Error($"Ayar dosyası okunamadı: {path}", ex);
            return null;
        }
    }

    /// <summary>"0x1038", "1038" veya "4152" gibi değerleri sayıya çevirir.</summary>
    public static int? ParseHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) ? hex : null;
        }

        return int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : null;
    }
}
