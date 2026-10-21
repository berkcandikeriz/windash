using System.Diagnostics;
using Microsoft.Win32;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>
/// Windows Gece Işığı. Microsoft bunun için genel bir API sunmuyor; durum, Ayarlar uygulamasının
/// kullandığı CloudStore kaydında ikili (binary) bir blok olarak tutuluyor.
///
/// Güvenlik önlemleri:
///  - Blok yalnızca bilinen iki biçimden birine BİREBİR uyuyorsa değiştirilir (41 bayt kapalı / 43 bayt açık).
///    Uymuyorsa hiçbir şey yazılmaz; Ayarlar > Gece ışığı sayfası açılır.
///  - İlk değişiklikten önce orijinal blok %LOCALAPPDATA%\WinDash\state.json içine yedeklenir.
///  - appsettings.json → Controls.NightLightMode = "settings" ile kayıt defterine hiç dokunulmaz.
/// </summary>
public sealed class NightLightService
{
    private const string StateKey =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\" +
        @"default$windows.data.bluelightreduction.bluelightreductionstate\" +
        @"windows.data.bluelightreduction.bluelightreductionstate";

    private const int OffLength = 41;
    private const int OnLength = 43;
    private const int StateIndex = 18;
    private const byte OnMarker = 0x15;
    private const byte OffMarker = 0x13;

    private readonly bool _allowWrite;

    public NightLightService(string mode)
    {
        _allowWrite = !string.Equals(mode, "settings", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>true/false: açık/kapalı. null: biçim tanınmadı veya kayıt yok.</summary>
    public bool? IsEnabled()
    {
        var data = Read();
        return data is null ? null : Parse(data);
    }

    /// <summary>Gece Işığı'nı değiştirir. Başarılıysa yeni durumu, değilse null döndürür (Ayarlar açılır).</summary>
    public bool? Toggle()
    {
        if (!_allowWrite)
        {
            OpenSettings();
            return null;
        }

        var data = Read();
        var state = data is null ? null : Parse(data);
        if (data is null || state is null)
        {
            Log.Warn("Gece Işığı kaydı tanınmadı; değişiklik yapılmadı. Blok: " +
                     (data is null ? "(yok)" : Convert.ToHexString(data)));
            OpenSettings();
            return null;
        }

        var app = AppState.Current;
        if (app.NightLightBackup is null)
        {
            app.NightLightBackup = Convert.ToBase64String(data);
            app.Save();
        }

        var updated = state.Value ? TurnOff(data) : TurnOn(data);
        BumpTimestamp(updated);

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKey, writable: true);
            if (key is null) return null;
            key.SetValue("Data", updated, RegistryValueKind.Binary);
            return !state.Value;
        }
        catch (Exception ex)
        {
            Log.Error("Gece Işığı ayarlanamadı", ex);
            return null;
        }
    }

    public static void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:nightlight") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Gece ışığı ayarları açılamadı", ex);
        }
    }

    private static byte[]? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKey);
            return key?.GetValue("Data") as byte[];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Bilinen biçim: "CB" başlığı (0x43 0x42 0x01 0x00), 18. baytta durum işareti.
    /// Açıkken 23-24. baytlarda fazladan 0x10 0x00 bulunur ve blok 2 bayt uzundur.
    /// </summary>
    private static bool? Parse(byte[] d)
    {
        if (d.Length < 25 || d[0] != 0x43 || d[1] != 0x42 || d[2] != 0x01 || d[3] != 0x00) return null;

        if (d.Length == OnLength && d[StateIndex] == OnMarker && d[23] == 0x10 && d[24] == 0x00) return true;
        if (d.Length == OffLength && d[StateIndex] == OffMarker) return false;
        return null;
    }

    private static byte[] TurnOn(byte[] off)
    {
        var on = new byte[OnLength];
        Array.Copy(off, 0, on, 0, 23);
        on[23] = 0x10;
        on[24] = 0x00;
        Array.Copy(off, 23, on, 25, off.Length - 23);
        on[StateIndex] = OnMarker;
        return on;
    }

    private static byte[] TurnOff(byte[] on)
    {
        var off = new byte[OffLength];
        Array.Copy(on, 0, off, 0, 23);
        Array.Copy(on, 25, off, 23, on.Length - 25);
        off[StateIndex] = OffMarker;
        return off;
    }

    /// <summary>
    /// Windows değişikliği fark etsin diye bloktaki zaman damgasını (10-14. baytlar, LEB128 sayı) artırır.
    /// Her baytın 7 bitlik değer kısmı taşmadan artırılır; devam biti (0x80) hiç değişmez.
    /// </summary>
    private static void BumpTimestamp(byte[] d)
    {
        for (var i = 10; i < 15; i++)
        {
            if ((d[i] & 0x7F) == 0x7F) continue;
            d[i]++;
            break;
        }
    }
}
