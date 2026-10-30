using System.Runtime.InteropServices;
using System.Windows.Interop;
using PcDashboard.Core;
using PcDashboard.Interop;

namespace PcDashboard.Services;

/// <summary>Tepsi menüsündeki bir satır. <see cref="Separator"/> ayırıcı çizgidir.</summary>
public sealed record TrayMenuItem(int Id, string Text, bool Checked = false, bool IsDefault = false)
{
    public static TrayMenuItem Separator { get; } = new(0, "");
    public bool IsSeparator => Id == 0;
}

/// <summary>
/// Görev çubuğunun bildirim alanındaki (Wi-Fi ve ses simgelerinin yanındaki) WinDash simgesi.
/// WinForms kullanmadan, doğrudan Shell_NotifyIcon ile: tıklama, sağ tık menüsü ve bilgi balonu.
/// Explorer yeniden başlarsa (TaskbarCreated) simge kendiliğinden yeniden eklenir.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int IconId = 1;
    private const int CallbackMessage = NativeMethods.WM_APP + 1;

    private readonly HwndSource _source;
    private readonly int _taskbarCreatedMessage;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly string _tooltip;
    private bool _added;
    private bool _disposed;

    /// <summary>Simgeye sol tık.</summary>
    public event EventHandler? Click;

    /// <summary>Simgeye sağ tık (menüyü göstermek dinleyicinin işi: <see cref="ShowMenu"/>).</summary>
    public event EventHandler? RightClick;

    /// <summary>Bilgi balonuna tıklandı.</summary>
    public event EventHandler? BalloonClick;

    /// <summary>Explorer yeniden başladı (görev çubuğu ve masaüstü yeniden oluşturuldu).</summary>
    public event EventHandler? ExplorerRestarted;

    /// <summary>Gizli pencereye dışarıdan WM_CLOSE geldi (ör. kurulum programı uygulamayı kapatmak istiyor).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Kayıtlı genel klavye kısayoluna basıldı (Id: <see cref="RegisterHotkey"/>).</summary>
    public event EventHandler<int>? HotkeyPressed;

    private readonly HashSet<int> _hotkeys = new();

    public IntPtr Handle => _source.Handle;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;

        // Görünmeyen, görev çubuğunda yer almayan bir ileti penceresi
        _source = new HwndSource(new HwndSourceParameters("WinDashTray", 1, 1)
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)NativeMethods.WS_EX_TOOLWINDOW,
        });
        _source.AddHook(WndProc);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        (_icon, _ownsIcon) = LoadIcon();
        EnableDarkMenus();
        Add();
    }

    private NativeMethods.NOTIFYICONDATA CreateData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
        hWnd = _source.Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void Add()
    {
        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP);
        _added = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (!_added)
        {
            // Explorer henüz hazır değilse (oturum açılışı) TaskbarCreated geldiğinde yeniden denenir
            Log.Warn("Bildirim alanı simgesi eklenemedi; görev çubuğu hazır olunca yeniden denenecek.");
        }
    }

    /// <summary>Simgenin üzerinden kısa bir Windows bildirimi gösterir.</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        var data = CreateData(NativeMethods.NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>Fare imlecinin olduğu yerde Windows menüsü açar; seçilen satırın Id'sini (seçilmezse 0) döndürür.</summary>
    public int ShowMenu(IEnumerable<TrayMenuItem> items)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero) return 0;

        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
                    continue;
                }

                var flags = NativeMethods.MF_STRING | (item.Checked ? NativeMethods.MF_CHECKED : 0);
                NativeMethods.AppendMenu(menu, flags, (UIntPtr)(uint)item.Id, item.Text);
                if (item.IsDefault) NativeMethods.SetMenuDefaultItem(menu, (uint)item.Id, 0);
            }

            NativeMethods.GetCursorPos(out var p);
            // Menünün dışına tıklanınca kapanması için pencerenin önde olması gerekir (Windows kuralı)
            NativeMethods.SetForegroundWindow(_source.Handle);
            var command = NativeMethods.TrackPopupMenuEx(menu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_NONOTIFY,
                p.X, p.Y, _source.Handle, IntPtr.Zero);
            NativeMethods.PostMessage(_source.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)(lParam.ToInt64() & 0xFFFF))
            {
                case NativeMethods.WM_LBUTTONUP:
                    Click?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WM_RBUTTONUP:
                    RightClick?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.NIN_BALLOONUSERCLICK:
                    BalloonClick?.Invoke(this, EventArgs.Empty);
                    break;
            }

            handled = true;
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            Log.Info("Görev çubuğu yeniden oluşturuldu; tepsi simgesi yeniden ekleniyor.");
            _added = false;
            Add();
            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_HOTKEY)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, (int)wParam.ToInt64());
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_CLOSE)
        {
            handled = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static (IntPtr Icon, bool Owned) LoadIcon()
    {
        try
        {
            // WinDash.exe'nin kendi ikonu (proje: ApplicationIcon), bildirim alanı boyutunda
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var small = new IntPtr[1];
                if (NativeMethods.ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                    return (small[0], true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Uygulama ikonu okunamadı: {ex.Message}");
        }

        return (NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION), false);
    }

    private static void EnableDarkMenus()
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 18362) return;
            NativeMethods.SetPreferredAppMode(1); // AllowDark: Windows koyu temadaysa menü de koyu
            NativeMethods.FlushMenuThemes();
        }
        catch
        {
            // Eski / farklı Windows: varsayılan açık menü
        }
    }

    /// <summary>
    /// "Win+Shift+L" gibi bir metni genel kısayol olarak kaydeder (her yerden çalışır).
    /// Başka bir uygulama aynı kısayolu kullanıyorsa false döner.
    /// </summary>
    public bool RegisterHotkey(int id, string? text)
    {
        UnregisterHotkey(id);
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!TryParseHotkey(text, out var modifiers, out var key))
        {
            Log.Warn($"Kısayol anlaşılamadı: '{text}' (ör. Win+Shift+L, Ctrl+Alt+L)");
            return false;
        }

        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, key))
        {
            Log.Warn($"Kısayol kaydedilemedi: '{text}' (başka bir uygulama kullanıyor olabilir)");
            return false;
        }

        _hotkeys.Add(id);
        return true;
    }

    public void UnregisterHotkey(int id)
    {
        if (_hotkeys.Remove(id)) NativeMethods.UnregisterHotKey(_source.Handle, id);
    }

    private static bool TryParseHotkey(string text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    continue;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    continue;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    continue;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    continue;
            }

            if (raw.Length == 1 && char.IsLetterOrDigit(raw[0]) && raw[0] < 128)
            {
                key = char.ToUpperInvariant(raw[0]);
            }
            else if ((raw[0] == 'F' || raw[0] == 'f') && int.TryParse(raw[1..], out var f) && f is >= 1 and <= 24)
            {
                key = (uint)(0x70 + f - 1);
            }
            else
            {
                return false;
            }
        }

        return key != 0 && modifiers != 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        foreach (var id in _hotkeys.ToList()) UnregisterHotkey(id);
        if (_ownsIcon && _icon != IntPtr.Zero) NativeMethods.DestroyIcon(_icon);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
