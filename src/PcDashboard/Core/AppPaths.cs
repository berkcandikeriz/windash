namespace PcDashboard.Core;

/// <summary>
/// WinDash'in dosya konumları.
///  - Kurulum klasörü (exe'nin yanı): varsayılan appsettings.json / layout.json — salt okunur sayılır.
///  - Kullanıcı klasörü %LOCALAPPDATA%\WinDash: kullanıcının düzenlediği ayarlar, state.json, log.txt.
/// Eski sürümlerin %LOCALAPPDATA%\PcDashboard klasörü ilk açılışta buraya taşınır.
/// </summary>
public static class AppPaths
{
    public const string ProductName = "WinDash";

    public static string InstallDirectory => AppContext.BaseDirectory;

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);

    private static string LegacyDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcDashboard");

    private static bool _prepared;

    /// <summary>Kullanıcı klasörünü oluşturur, eski veriyi taşır ve eksik ayar dosyalarını varsayılanlardan kopyalar.</summary>
    public static void Prepare()
    {
        if (_prepared) return;
        _prepared = true;

        try
        {
            Directory.CreateDirectory(DataDirectory);

            // Eski "PC Dashboard" sürümünden kalan seçimler
            foreach (var name in new[] { "state.json" })
            {
                var oldFile = Path.Combine(LegacyDataDirectory, name);
                var newFile = Path.Combine(DataDirectory, name);
                if (File.Exists(oldFile) && !File.Exists(newFile)) File.Copy(oldFile, newFile);
            }

            UpgradeLayout();

            foreach (var name in new[] { "appsettings.json", "layout.json" })
            {
                var target = Path.Combine(DataDirectory, name);
                if (File.Exists(target)) continue;

                var source = Path.Combine(InstallDirectory, name);
                if (File.Exists(source)) File.Copy(source, target);
            }
        }
        catch
        {
            // Kopyalanamazsa ayarlar kurulum klasöründen okunur (SettingsLoader yedeği)
        }
    }

    /// <summary>
    /// Yeni sürümle gelen yerleşim (ör. yeni bir kart) kullanıcının eski layout.json'ından daha yeniyse,
    /// eskisini layout.backup.json olarak saklayıp yenisini kopyalar.
    /// </summary>
    private static void UpgradeLayout()
    {
        var user = UserFile("layout.json");
        var source = DefaultFile("layout.json");
        if (!File.Exists(user) || !File.Exists(source)) return;

        var userVersion = SettingsLoader.ReadLayoutVersion(user);
        var defaultVersion = SettingsLoader.ReadLayoutVersion(source);
        if (defaultVersion <= userVersion) return;

        File.Copy(user, UserFile("layout.backup.json"), overwrite: true);
        File.Copy(source, user, overwrite: true);
        Log.Info($"Yerleşim güncellendi (sürüm {userVersion} → {defaultVersion}); eskisi layout.backup.json");
    }

    public static string UserFile(string name) => Path.Combine(DataDirectory, name);

    public static string DefaultFile(string name) => Path.Combine(InstallDirectory, name);
}
