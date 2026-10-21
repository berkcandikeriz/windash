using System.Text;
using System.Windows;
using System.Windows.Media;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // RSS beslemelerindeki windows-1254 gibi kod sayfaları ve netsh çıktısı için
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _singleInstance = new Mutex(true, @"Local\WinDash.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

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
        var autostart = e.Args.Any(a => a.Equals(Autostart.Argument, StringComparison.OrdinalIgnoreCase));
        Log.Info(autostart ? "WinDash Windows açılışında başlatılıyor" : "WinDash başlatılıyor");
        if (autostart) await WaitForDesktopAsync();

        var window = new MainWindow();
        // Oturum açılırken odağı çalmasın; ana ekranda çalışan uygulamanın önüne geçmesin
        if (autostart) window.ShowActivated = false;
        MainWindow = window;
        window.Show();
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
