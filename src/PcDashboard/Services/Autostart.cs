using Microsoft.Win32;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>
/// "Windows ile başlat": HKCU\Software\Microsoft\Windows\CurrentVersion\Run altına exe'yi
/// "--autostart" bağımsız değişkeniyle yazar. Yönetici izni gerekmez; Görev Yöneticisi > Başlangıç
/// uygulamaları listesinde "WinDash" olarak görünür ve oradan da kapatılabilir.
/// </summary>
public static class Autostart
{
    public const string Argument = "--autostart";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinDash";
    private const string LegacyValueName = "PcDashboard";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return;

            if (enabled && Environment.ProcessPath is { } exe)
            {
                key.SetValue(ValueName, $"\"{exe}\" {Argument}");
                Log.Info($"Windows ile başlat: açık ({exe})");
            }
            else if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Windows ile başlat: kapalı");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Otomatik başlatma ayarlanamadı", ex);
        }
    }

    /// <summary>
    /// Açılışta çağrılır. Öncelik: kartın sağ tık menüsünden yapılan seçim (state.json) &gt;
    /// appsettings.json → StartWithWindows: true. Hiçbiri yoksa kayda dokunulmaz
    /// (kurulum betiğinin eklediği kayıt silinmez). Açıksa yol güncellenir (exe taşındıysa).
    /// </summary>
    public static void Sync(bool settingsWantsAutostart)
    {
        MigrateLegacy();

        var choice = AppState.Current.StartWithWindows;
        if (choice is { } explicitChoice)
        {
            Set(explicitChoice);
        }
        else if (settingsWantsAutostart || IsEnabled())
        {
            Set(true);
        }
    }

    /// <summary>Eski "PcDashboard" adlı başlangıç kaydını kaldırır; açıksa WinDash adıyla yeniden yazılır.</summary>
    private static void MigrateLegacy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyValueName) is null) return;
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            if (AppState.Current.StartWithWindows is null) Set(true);
            Log.Info("Eski PcDashboard başlangıç kaydı WinDash olarak güncellendi");
        }
        catch (Exception ex)
        {
            Log.Warn($"Eski başlangıç kaydı taşınamadı: {ex.Message}");
        }
    }

    /// <summary>Sağ tık menüsündeki "Windows ile başlat" seçeneği.</summary>
    public static bool Toggle()
    {
        var enable = !IsEnabled();
        Set(enable);
        AppState.Current.StartWithWindows = enable;
        AppState.Current.Save();
        return enable;
    }
}
