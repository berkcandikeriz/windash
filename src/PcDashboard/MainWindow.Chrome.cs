using System.Windows;
using PcDashboard.Core;
using PcDashboard.Interop;
using PcDashboard.Services;

namespace PcDashboard;

/// <summary>
/// Pencere modları (düğmeleri Cihazlar kartının üstündeki "Pencere" kartındadır):
///  - Tam ekran: monitörün tamamı (görev çubuğu dahil). Kartta Masaüstü / Başlat / Ara da görünür.
///  - Büyütülmüş: monitörün görev çubuğu hariç alanı.
///  - Pencere: taşınabilir (kartı sürükle) ve kenarlardan yeniden boyutlandırılabilir; içerik oranını koruyarak ölçeklenir.
/// </summary>
public partial class MainWindow : IWindowHost
{
    private enum DisplayMode
    {
        FullScreen,
        Maximized,
        Windowed,
    }

    private DisplayMode _mode = DisplayMode.FullScreen;
    private DisplayMode _modeBeforeFullScreen = DisplayMode.Windowed;
    private MonitorInfo? _monitor;

    public event EventHandler? DisplayModeChanged;

    public bool IsFullScreen => _mode == DisplayMode.FullScreen;
    public bool IsMaximized => _mode == DisplayMode.Maximized;
    public bool IsWindowed => _mode == DisplayMode.Windowed;

    private void InitChrome()
    {
        _mode = AppState.Current.WindowMode?.ToLowerInvariant() switch
        {
            "maximized" => DisplayMode.Maximized,
            "windowed" => DisplayMode.Windowed,
            "fullscreen" => DisplayMode.FullScreen,
            // İlk açılış: ikinci bir ekran varsa onu tam ekran kapla; tek ekranda ana ekranı örtme, pencere aç
            _ => MonitorService.GetMonitors().Count >= 2 ? DisplayMode.FullScreen : DisplayMode.Windowed,
        };

        StateChanged += OnWindowStateChanged;
        Closing += (_, _) => SaveWindowedBounds();
    }

    // =====================================================================
    // Modlar
    // =====================================================================

    /// <summary>Pencereyi geçerli moda göre yerleştirir. Monitör yoksa / değiştiyse yeniden seçer.</summary>
    private void ApplyMode(bool pickCurrentMonitor = false)
    {
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0) return;

        if (pickCurrentMonitor)
        {
            _monitor = MonitorService.Current(this) ?? _monitor;
        }

        if (_monitor is null || monitors.All(m => m.Handle != _monitor.Handle))
        {
            Log.Info("Monitörler: " + string.Join(" | ", monitors));
            _monitor = MonitorService.Choose(monitors, _settings.Display);
            Log.Info($"Seçilen monitör: {_monitor}");
        }
        else
        {
            // Çözünürlük / görev çubuğu değişmiş olabilir; güncel ölçüleri al
            _monitor = monitors.First(m => m.Handle == _monitor.Handle);
        }

        if (_monitor is null) return;

        switch (_mode)
        {
            case DisplayMode.FullScreen:
                ResizeMode = ResizeMode.NoResize;
                Topmost = _settings.Display.Topmost;
                MonitorService.Place(this, _monitor);
                break;

            case DisplayMode.Maximized:
                ResizeMode = ResizeMode.NoResize;
                Topmost = false;
                MonitorService.Place(this, _monitor.Work);
                break;

            case DisplayMode.Windowed:
                ResizeMode = ResizeMode.CanResize;
                Topmost = false;
                MonitorService.Place(this, SavedWindowedBounds() ?? DefaultWindowedBounds(_monitor));
                break;
        }

        DisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SwitchMode(DisplayMode mode)
    {
        if (mode == _mode) return;
        if (_mode == DisplayMode.Windowed) SaveWindowedBounds();
        if (mode == DisplayMode.FullScreen) _modeBeforeFullScreen = _mode;

        _mode = mode;
        AppState.Current.WindowMode = mode.ToString().ToLowerInvariant();
        AppState.Current.Save();

        // Pencere başka monitöre taşındıysa tam ekran / büyütme oraya yapılsın
        ApplyMode(pickCurrentMonitor: true);
    }

    private static Int32Rect DefaultWindowedBounds(MonitorInfo m)
    {
        var width = (int)(m.Work.Width * 0.7);
        var height = width * 9 / 16;
        if (height > m.Work.Height * 0.85)
        {
            height = (int)(m.Work.Height * 0.85);
            width = height * 16 / 9;
        }

        return new Int32Rect(m.Work.X + (m.Work.Width - width) / 2, m.Work.Y + (m.Work.Height - height) / 2, width, height);
    }

    private static Int32Rect? SavedWindowedBounds()
    {
        if (AppState.Current.WindowBounds is not { Length: 4 } b || b[2] < 320 || b[3] < 180) return null;
        var rect = new Int32Rect(b[0], b[1], b[2], b[3]);

        // Kaydedilen konum hâlâ bir monitörün üzerinde mi? (Monitör çıkarılmış olabilir)
        var visible = MonitorService.GetMonitors().Any(m =>
            rect.X < m.X + m.Width - 80 && rect.X + rect.Width > m.X + 80 &&
            rect.Y < m.Y + m.Height - 40 && rect.Y >= m.Y - 20);
        return visible ? rect : null;
    }

    private void SaveWindowedBounds()
    {
        if (_mode != DisplayMode.Windowed || WindowState != WindowState.Normal) return;
        if (MonitorService.Bounds(this) is not { } r) return;
        AppState.Current.WindowBounds = new[] { r.X, r.Y, r.Width, r.Height };
        AppState.Current.Save();
    }

    /// <summary>Win+Yukarı gibi kısayollarla Windows'un "büyüt"ü kenarlıksız pencerede görev çubuğunu örter; bizim büyütmemize çevir.</summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Maximized) return;
        WindowState = WindowState.Normal;
        SwitchMode(DisplayMode.Maximized);
    }

    // =====================================================================
    // IWindowHost (Pencere kartı çağırır)
    // =====================================================================

    public void MinimizeWindow() => WindowState = WindowState.Minimized;

    public void ToggleMaximize() =>
        SwitchMode(_mode == DisplayMode.Maximized ? DisplayMode.Windowed : DisplayMode.Maximized);

    public void ToggleFullScreen() =>
        SwitchMode(_mode == DisplayMode.FullScreen
            ? (_modeBeforeFullScreen == DisplayMode.FullScreen ? DisplayMode.Windowed : _modeBeforeFullScreen)
            : DisplayMode.FullScreen);

    public void CloseWindow() => Close();

    public void BeginDrag()
    {
        if (_mode != DisplayMode.Windowed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // fare bırakılmış
        }
    }

    /// <summary>Masaüstüne dön (görev çubuğundaki "Masaüstünü göster" ile aynı).</summary>
    public void ShowDesktop()
    {
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            shell.ToggleDesktop();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        catch (Exception ex)
        {
            Log.Error("Masaüstü gösterilemedi", ex);
        }
    }

    /// <summary>Başlat menüsü (Windows tuşu).</summary>
    public void OpenStartMenu() => SendKeys(NativeMethods.VK_LWIN);

    /// <summary>Windows Arama (Win+S).</summary>
    public void OpenSearch() => SendKeys(NativeMethods.VK_LWIN, NativeMethods.VK_S);

    private static void SendKeys(params byte[] keys)
    {
        // Sırayla bas, ters sırayla bırak (ör. Win+S: Win↓ S↓ S↑ Win↑)
        foreach (var k in keys) NativeMethods.keybd_event(k, 0, 0, UIntPtr.Zero);
        for (var i = keys.Length - 1; i >= 0; i--) NativeMethods.keybd_event(keys[i], 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
