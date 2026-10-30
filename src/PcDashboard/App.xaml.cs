using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard;

/// <summary>
/// Uygulamanın yaşam döngüsü:
///  - WinDash penceresi küçültülünce / kapatılınca gizlenir, uygulama bildirim alanında (tepsi) çalışmaya devam eder.
///  - "Masaüstüne göm" açıkken pencere gizlenir, seçilen kartlar masaüstünün sağında durur (DesktopWindow).
///  - Gerçekten çıkmak için: tepsi menüsü / sağ tık menüsü > Çıkış veya Ctrl+Q.
/// </summary>
public partial class App : Application
{
    private const string ShowSignalName = @"Local\WinDash.Show";

    private const int CmdOpen = 1;
    private const int CmdDesktop = 2;
    private const int CmdAutostart = 3;
    private const int CmdReload = 4;
    private const int CmdSettings = 5;
    private const int CmdLock = 6;
    private const int CmdEdit = 7;
    private const int CmdExit = 9;
    private const int LockHotkeyId = 1;

    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showSignalWait;
    private TrayIcon? _tray;
    private MainWindow? _dashboard;
    private bool _dashboardClosed;
    private DesktopWindow? _desktop;
    private StandbyService? _standby;
    private bool _lockHotkeyRegistered;

    /// <summary>Uygulamanın o anki ayarları (Yenile ile yeniden okunur).</summary>
    public AppSettings Settings { get; private set; } = new();
    private bool _exited;

    public static new App Current => (App)Application.Current;

    /// <summary>Uygulama kapanıyor; pencereler artık gizlenmek yerine kapanmalı.</summary>
    public bool IsExiting { get; private set; }

    /// <summary>"Masaüstüne göm" açık mı.</summary>
    public bool IsDesktopMode => AppState.Current.DesktopMode;

    /// <summary>Masaüstüne gömme açılıp kapanınca (Pencere kartındaki düğmenin görünümü için).</summary>
    public event EventHandler? DesktopModeChanged;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // RSS beslemelerindeki windows-1254 gibi kod sayfaları ve netsh çıktısı için
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var autostart = e.Args.Any(a => a.Equals(Autostart.Argument, StringComparison.OrdinalIgnoreCase));

        _singleInstance = new Mutex(true, @"Local\WinDash.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            // Zaten çalışıyor (belki tepside gizli): Başlat menüsünden açıldıysa onun penceresini öne getir
            if (!autostart && EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
            {
                signal.Set();
                signal.Dispose();
            }

            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SessionEnding += (_, _) => IsExiting = true;

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Beklenmeyen hata (UI)", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Beklenmeyen hata", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Gözlemlenmeyen görev hatası", args.Exception);
            args.SetObserved();
        };

        AppPaths.Prepare();

        // İpuçları kısa bir gecikmeyle açılsın ve okunacak kadar kalsın
        System.Windows.Controls.ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(350));
        System.Windows.Controls.ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(8000));
        Log.Info(autostart ? "WinDash Windows açılışında başlatılıyor" : "WinDash başlatılıyor");
        Settings = SettingsLoader.LoadSettings();
        if (autostart) await WaitForDesktopAsync();

        _tray = new TrayIcon("WinDash");
        _tray.Click += (_, _) => ShowDashboard();
        _tray.BalloonClick += (_, _) => ShowDashboard();
        _tray.RightClick += (_, _) => ShowTrayMenu();
        _tray.CloseRequested += (_, _) => BeginExit();
        _tray.ExplorerRestarted += (_, _) => OnExplorerRestarted();
        _tray.HotkeyPressed += (_, id) =>
        {
            if (id == LockHotkeyId) LockWithStandby();
        };
        RegisterLockHotkey();

        _standby = new StandbyService(() => Settings.Standby);
        _standby.Unlocked += (_, _) => OnSignedIn();

        // Oturum açılışında (Windows ile başlatıldıysa) doğrudan masaüstüne gömülü başla
        if (autostart && Settings.Standby.DesktopModeAfterSignIn && !AppState.Current.DesktopMode)
        {
            AppState.Current.DesktopMode = true;
            AppState.Current.Save();
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showSignalWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.InvokeAsync(ShowDashboard), null, Timeout.Infinite, false);

        var window = new MainWindow();
        window.Closed += (_, _) => _dashboardClosed = true;
        // Oturum açılırken odağı çalmasın; ana ekranda çalışan uygulamanın önüne geçmesin
        if (autostart) window.ShowActivated = false;
        MainWindow = _dashboard = window;

        if (IsDesktopMode)
        {
            // Pencere gizli kalır (ilk açıldığında yüklenir); kartlar masaüstünde
            ShowDesktopPanel();
        }
        else
        {
            window.Show();
        }
    }

    // =====================================================================
    // Pencere / tepsi / masaüstü
    // =====================================================================

    /// <summary>WinDash penceresini gösterir ve öne getirir (tepsi simgesine tık, Başlat menüsünden yeniden açma).</summary>
    public void ShowDashboard()
    {
        if (IsExiting || _dashboard is null || _dashboardClosed) return;
        _dashboard.ShowDashboard();
    }

    /// <summary>Pencereyi gizler; uygulama tepside çalışmaya devam eder.</summary>
    public void HideDashboard()
    {
        if (IsExiting || _dashboard is null || _dashboardClosed) return;
        _dashboard.HideToTray();
        ShowBackgroundHint(IsDesktopMode
            ? "WinDash masaüstüne gömüldü"
            : "WinDash arka planda çalışıyor");
    }

    /// <summary>İlk gizlenişte bir kez: nereden açılacağını ve nasıl çıkılacağını anlatan bildirim.</summary>
    private void ShowBackgroundHint(string title)
    {
        if (AppState.Current.TrayHintShown || _tray is null) return;
        _tray.ShowBalloon(title,
            "Açmak için bildirim alanındaki WinDash simgesine tıklayın. Tamamen kapatmak için simgeye sağ tık > Çıkış.");
        AppState.Current.TrayHintShown = true;
        AppState.Current.Save();
    }

    /// <summary>"Masaüstüne göm" aç / kapat. Açınca pencere gizlenir, kartlar masaüstüne yerleşir.</summary>
    public void SetDesktopMode(bool enabled)
    {
        if (IsExiting) return;
        if (AppState.Current.DesktopMode != enabled)
        {
            AppState.Current.DesktopMode = enabled;
            AppState.Current.Save();
            Log.Info(enabled ? "Masaüstüne gömme açıldı" : "Masaüstüne gömme kapatıldı");
        }

        if (enabled)
        {
            ShowDesktopPanel();
            HideDashboard();
        }
        else
        {
            CloseDesktopPanel();
            ShowDashboard();
        }

        DesktopModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowDesktopPanel()
    {
        if (_desktop is not null || IsExiting) return;
        try
        {
            var panel = new DesktopWindow();
            panel.Closed += OnDesktopPanelClosed;
            _desktop = panel;
            panel.Show();
        }
        catch (Exception ex)
        {
            Log.Error("Masaüstü paneli açılamadı", ex);
            _desktop = null;
        }
    }

    private void CloseDesktopPanel()
    {
        var panel = _desktop;
        if (panel is null) return;
        _desktop = null;
        panel.ClosingByApp = true;
        panel.Close();
    }

    /// <summary>Panel bizden habersiz kapandıysa (Explorer yeniden başladı vb.) birkaç saniye sonra yeniden aç.</summary>
    private void OnDesktopPanelClosed(object? sender, EventArgs e)
    {
        if (sender is not DesktopWindow panel || panel.ClosingByApp || IsExiting) return;
        if (ReferenceEquals(_desktop, panel)) _desktop = null;
        if (!IsDesktopMode) return;

        Log.Warn("Masaüstü paneli beklenmedik şekilde kapandı; yeniden açılıyor.");
        RunLater(TimeSpan.FromSeconds(3), ShowDesktopPanel);
    }

    /// <summary>Explorer yeniden başlayınca masaüstü penceresi değişir; paneli yenisine yeniden bağla.</summary>
    private void OnExplorerRestarted()
    {
        if (!IsDesktopMode || IsExiting) return;
        RunLater(TimeSpan.FromSeconds(2), () =>
        {
            CloseDesktopPanel();
            if (IsDesktopMode) ShowDesktopPanel();
        });
    }

    private void RunLater(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsExiting) action();
        };
        timer.Start();
    }

    /// <summary>Ayarları ve yerleşimi her iki yerde de yeniden yükler (F5 / menü > Yenile).</summary>
    public async void ReloadAll()
    {
        try
        {
            Settings = SettingsLoader.LoadSettings();
            RegisterLockHotkey();
            if (_dashboard is not null && !_dashboardClosed) await _dashboard.ReloadAsync();
            if (_desktop is not null) await _desktop.ReloadAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Yenileme başarısız", ex);
        }
    }

    public void ShowTrayMenu()
    {
        if (_tray is null || IsExiting) return;

        var command = _tray.ShowMenu(new[]
        {
            new TrayMenuItem(CmdOpen, "WinDash'i aç", IsDefault: true),
            new TrayMenuItem(CmdDesktop, "Masaüstüne göm", Checked: IsDesktopMode),
            new TrayMenuItem(CmdEdit, "Düzenle (kartları taşı / boyutlandır)"),
            new TrayMenuItem(CmdLock, _lockHotkeyRegistered
                ? $"WinDash ile kilitle\t{Settings.Standby.LockHotkey}"
                : "WinDash ile kilitle"),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdAutostart, "Windows ile başlat", Checked: Autostart.IsEnabled()),
            new TrayMenuItem(CmdReload, "Yenile"),
            new TrayMenuItem(CmdSettings, "Ayar klasörünü aç"),
            TrayMenuItem.Separator,
            new TrayMenuItem(CmdExit, "Çıkış"),
        });

        switch (command)
        {
            case CmdOpen:
                ShowDashboard();
                break;
            case CmdDesktop:
                SetDesktopMode(!IsDesktopMode);
                break;
            case CmdAutostart:
                Autostart.Toggle();
                break;
            case CmdReload:
                ReloadAll();
                break;
            case CmdSettings:
                OpenShell(SettingsLoader.BaseDirectory);
                break;
            case CmdEdit:
                EditLayout();
                break;
            case CmdLock:
                LockWithStandby();
                break;
            case CmdExit:
                ExitApp();
                break;
        }
    }

    /// <summary>Düzenle: masaüstüne gömülüyse masaüstü panelini, değilse WinDash penceresini düzenler.</summary>
    public void EditLayout()
    {
        if (IsExiting) return;
        if (IsDesktopMode && _desktop is not null)
        {
            _desktop.BeginEdit();
            return;
        }

        if (_dashboard is null || _dashboardClosed) return;
        ShowDashboard();
        // Pencere ilk kez açılıyorsa kartlar yüklendikten sonra
        Dispatcher.InvokeAsync(() => _dashboard?.BeginEdit(), DispatcherPriority.ContextIdle);
    }

    // =====================================================================
    // Bekleme ekranı ve kilit
    // =====================================================================

    /// <summary>Bekleme ekranını açar; dokunulunca Windows kilitlenir (parola / Oturum aç ekranı).</summary>
    public void LockWithStandby()
    {
        if (IsExiting || _standby is null) return;
        _standby.LockWithStandby();
    }

    private void RegisterLockHotkey()
    {
        if (_tray is null) return;
        _lockHotkeyRegistered = _tray.RegisterHotkey(LockHotkeyId, Settings.Standby.LockHotkey);
        if (_lockHotkeyRegistered) Log.Info($"Kilit kısayolu: {Settings.Standby.LockHotkey}");
    }

    /// <summary>Kilit açıldı / oturum açıldı: ayara göre masaüstüne gömülü moda geç.</summary>
    private void OnSignedIn()
    {
        if (IsExiting || !Settings.Standby.DesktopModeAfterSignIn) return;
        if (!IsDesktopMode) SetDesktopMode(true);
        else if (_dashboard is { IsVisible: true }) HideDashboard();
    }

    public static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Açılamadı: {target}", ex);
        }
    }

    /// <summary>Bir pencerenin kapanma olayının içinden çağrılabilen çıkış (asıl iş bir sonraki turda yapılır).</summary>
    public void BeginExit()
    {
        if (IsExiting) return;
        IsExiting = true;
        Dispatcher.InvokeAsync(ExitApp);
    }

    /// <summary>Uygulamadan tamamen çık: paneli ve pencereyi kapat, tepsi simgesini kaldır.</summary>
    public void ExitApp()
    {
        if (_exited) return;
        _exited = true;
        IsExiting = true;
        Log.Info("WinDash kapatılıyor");

        try
        {
            CloseDesktopPanel();
            if (_dashboard is not null && !_dashboardClosed) _dashboard.Close();
        }
        catch (Exception ex)
        {
            Log.Error("Pencereler kapatılırken hata", ex);
        }

        _standby?.Dispose();
        _standby = null;
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    /// <summary>
    /// Oturum açılışında masaüstü, ikinci ekran (USB-C / HDMI) ve ağ biraz geç hazır olabilir.
    /// Birkaç saniye bekler; ikinci ekran tercih ediliyorsa 30 saniyeye kadar onun gelmesini bekler.
    /// (Ekran sonradan bağlanırsa pencere zaten kendiliğinden ona taşınır.)
    /// </summary>
    private static async Task WaitForDesktopAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));

        var display = SettingsLoader.LoadSettings().Display;
        if (!display.PreferSecondary && string.IsNullOrWhiteSpace(display.MonitorDeviceName)) return;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline && MonitorService.GetMonitors().Count < 2)
            await Task.Delay(TimeSpan.FromSeconds(1));

        Log.Info($"Açılışta bulunan monitör sayısı: {MonitorService.GetMonitors().Count}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _showSignalWait?.Unregister(null);
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Vurgu rengi ve kart saydamlığını ayarlardan uygular.</summary>
    public static void ApplyTheme(AppSettings s)
    {
        var resources = Current.Resources;

        var accent = ParseColor(s.Accent, Color.FromRgb(0x8A, 0xB4, 0xF8));
        resources["AccentBrush"] = Frozen(new SolidColorBrush(accent));

        var tint = ParseColor(s.CardTint, Color.FromRgb(0x16, 0x18, 0x22));
        tint.A = (byte)Math.Clamp(s.CardOpacity * 255, 0, 255);
        resources["CardBackgroundBrush"] = Frozen(new SolidColorBrush(tint));
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
