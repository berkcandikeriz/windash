using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <param name="FormFactor">Windows'un bildirdiği aygıt biçimi (bkz. <see cref="AudioHub.FormFactorText"/>).</param>
public sealed record AudioEndpoint(string Id, string FriendlyName, int FormFactor)
{
    public bool IsHeadphones => FormFactor is AudioHub.FormHeadphones or AudioHub.FormHeadset;

    /// <summary>Yazılımın oluşturduğu sanal ses aygıtı mı (SteelSeries Sonar, VB-Audio, Voicemeeter…)?</summary>
    public bool IsVirtual => AudioHub.IsVirtualName(FriendlyName);
}

/// <summary>Ses aygıtlarına erişim (Core Audio / NAudio) ve varsayılan aygıtı değiştirme.</summary>
public static class AudioHub
{
    // PKEY_AudioEndpoint_FormFactor değerleri
    public const int FormSpeakers = 1;
    public const int FormLineLevel = 2;
    public const int FormHeadphones = 3;
    public const int FormMicrophone = 4;
    public const int FormHeadset = 5;
    public const int FormHandset = 6;
    public const int FormSpdif = 8;
    public const int FormDisplay = 9;

    private static readonly PropertyKey FormFactorKey = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);

    /// <summary>Ses yongası adları; bunlarda ürün adı yerine uç nokta adı ("Realtek Digital Output") daha anlamlı.</summary>
    private static readonly string[] ChipsetNames =
        { "Realtek", "High Definition Audio", "NVIDIA", "AMD", "Intel", "Conexant", "VIA HD", "IDT" };

    private static MMDeviceEnumerator? _enumerator;

    /// <summary>appsettings.json → Devices.AudioAliases (MainWindow yükler).</summary>
    public static List<AudioAlias> Aliases { get; set; } = new();

    private static MMDeviceEnumerator Enumerator => _enumerator ??= new MMDeviceEnumerator();

    public static List<AudioEndpoint> List(DataFlow flow)
    {
        var list = new List<AudioEndpoint>();
        try
        {
            foreach (var device in Enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device) list.Add(new AudioEndpoint(device.ID, device.FriendlyName, FormFactor(device)));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ses aygıtları listelenemedi", ex);
        }

        return list;
    }

    public static MMDevice? GetById(string id)
    {
        try
        {
            var device = Enumerator.GetDevice(id);
            return device.State == DeviceState.Active ? device : null;
        }
        catch
        {
            return null;
        }
    }

    public static int FormFactor(MMDevice device)
    {
        try
        {
            var store = device.Properties;
            return store.Contains(FormFactorKey) ? Convert.ToInt32(store[FormFactorKey].Value) : -1;
        }
        catch
        {
            return -1;
        }
    }

    private static readonly string[] VirtualWords =
    {
        "Sonar", "Virtual", "VB-Audio", "Voicemeeter", "CABLE", "Broadcast", "Steam Streaming",
        "Wave Link", "Krisp", "Nahimic", "Discord", "OBS", "Sanal",
    };

    /// <summary>Adından yazılımsal (sanal) ses aygıtı olduğu anlaşılıyor mu?</summary>
    public static bool IsVirtualName(string friendlyName) =>
        VirtualWords.Any(w => friendlyName.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Aygıt biçiminin okunur adı (alt yazı için).</summary>
    public static string FormFactorText(int formFactor) => formFactor switch
    {
        FormSpeakers => "Hoparlör",
        FormLineLevel => "Hat çıkışı",
        FormHeadphones => "Kulaklık",
        FormMicrophone => "Mikrofon",
        FormHeadset => "Mikrofonlu kulaklık",
        FormHandset => "Ahize",
        FormSpdif => "Optik (S/PDIF)",
        FormDisplay => "HDMI / DisplayPort",
        _ => "",
    };

    /// <summary>Adında verilen ifadelerden biri geçen ilk etkin aygıt.</summary>
    public static MMDevice? Find(DataFlow flow, IReadOnlyCollection<string> nameContains)
    {
        if (nameContains.Count == 0) return null;
        var match = List(flow).FirstOrDefault(d =>
            nameContains.Any(n => d.FriendlyName.Contains(n, StringComparison.OrdinalIgnoreCase)));
        return match is null ? null : GetById(match.Id);
    }

    public static MMDevice? GetDefault(DataFlow flow)
    {
        try
        {
            return Enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)
                ? Enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? DefaultId(DataFlow flow)
    {
        using var device = GetDefault(flow);
        return device?.ID;
    }

    public static string? DefaultOutputName()
    {
        using var device = GetDefault(DataFlow.Render);
        return device?.FriendlyName;
    }

    /// <summary>
    /// Windows'un varsayılan ses aygıtını değiştirir (Ses ayarlarındaki "Varsayılan yap" ile aynı).
    /// Belgelenmemiş ama Windows 7'den beri değişmeyen IPolicyConfig arayüzü kullanılır.
    /// </summary>
    public static bool SetDefault(string deviceId)
    {
        try
        {
            var config = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (var role in new[] { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications })
                {
                    var hr = config.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(config);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Varsayılan ses aygıtı değiştirilemedi", ex);
            return false;
        }
    }

    /// <summary>Takma ad tanımlıysa onu, yoksa kısaltılmış Windows adını döndürür.</summary>
    public static (string Name, string? Subtitle) Display(string friendlyName)
    {
        var alias = Aliases.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Contains) && friendlyName.Contains(a.Contains, StringComparison.OrdinalIgnoreCase));
        return alias is null
            ? (ShortName(friendlyName), null)
            : (alias.Name, string.IsNullOrWhiteSpace(alias.Subtitle) ? null : alias.Subtitle);
    }

    /// <summary>
    /// Windows adı "Uç nokta (Ürün)" biçimindedir. USB / Bluetooth aygıtlarda parantez içi ürün adıdır
    /// ("Mikrofon (2- AM8)" → "AM8"); ses yongalarında ise uç nokta adı daha anlamlıdır
    /// ("Realtek Digital Output (Realtek(R) Audio)" → "Realtek Digital Output").
    /// </summary>
    public static string ShortName(string friendlyName)
    {
        var open = friendlyName.IndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && friendlyName.EndsWith(')'))
        {
            var outer = friendlyName[..open].Trim();
            var inner = friendlyName[(open + 2)..^1].Trim();
            // "2- AM8" gibi Windows'un eklediği sıra numaralarını temizle
            var dash = inner.IndexOf("- ", StringComparison.Ordinal);
            if (dash is > 0 and <= 2 && char.IsDigit(inner[0])) inner = inner[(dash + 2)..];

            var chipset = ChipsetNames.Any(c => inner.Contains(c, StringComparison.OrdinalIgnoreCase));
            return chipset && outer.Length > 0 ? outer : inner;
        }

        return friendlyName;
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    [ComImport]
    [Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient
    {
    }

    [ComImport]
    [Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
