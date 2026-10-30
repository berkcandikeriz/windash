using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcDashboard.Core;
using PcDashboard.Interop;

namespace PcDashboard.Services;

/// <summary>
/// Windows'un o anki masaüstü arka planını bulur, bulanıklaştırır ve taslaktaki cam efektini
/// (kartların arkasındaki ek bulanıklık + %180 doygunluk + gölge) tek bir bitmap'e işler.
/// Her şey açılışta / duvar kâğıdı değişince bir kez hesaplanır; çalışırken GPU/CPU maliyeti yoktur.
/// </summary>
public sealed class WallpaperService : IDisposable
{
    public const double DesignWidth = 1920;
    public const double DesignHeight = 1080;
    private const int SmallWidth = 640;
    private const int SmallHeight = 360;
    private const double SmallScale = SmallWidth / DesignWidth;

    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly Dispatcher _dispatcher;
    private string _signature = "";
    private bool _disposed;

    /// <summary>
    /// Duvar kâğıdı değişince (Ayarlar, slayt gösterisi, Spotlight, monitöre özel resim, yerleştirme biçimi) tetiklenir.
    /// Windows'un bildirimi gelince kısa bir gecikmeyle (dosya yazılırken eskisini okumamak için) ve ayrıca
    /// 10 saniyede bir dosya / ayar karşılaştırmasıyla yakalanır.
    /// </summary>
    public event EventHandler? Changed;

    public WallpaperService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _watchTimer.Tick += (_, _) => CheckForChange();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.Desktop) return;
        _dispatcher.InvokeAsync(() =>
        {
            // Windows yeni resmi biraz sonra yazabiliyor: önce kısa gecikmeyle yenile, sonra bir kez daha kontrol et
            After(TimeSpan.FromMilliseconds(700), Raise);
            After(TimeSpan.FromSeconds(3), CheckForChange);
        });
    }

    private void After(TimeSpan delay, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_disposed) action();
        };
        timer.Start();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    public static string? GetCurrentWallpaperPath()
    {
        try
        {
            var sb = new StringBuilder(520);
            if (NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
            {
                var path = sb.ToString();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Duvar kâğıdı yolu okunamadı", ex);
        }

        // Windows, kullanılan duvar kâğıdının bir kopyasını burada tutar (slayt gösterisi, Spotlight vb.)
        var transcoded = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    /// <summary>
    /// Görseli okuyup 640x360 boyutunda bulanıklaştırılmış bir bitmap döndürür
    /// (CSS: filter: blur(60px) + transform: scale(1.04)).
    /// </summary>
    public static BitmapSource? CreateBlurred(string path, double blurRadiusAtDesignSize)
    {
        try
        {
            // Dosyayı kilitlememek için önce belleğe al
            var bytes = File.ReadAllBytes(path);
            var source = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                source.BeginInit();
                source.CacheOption = BitmapCacheOption.OnLoad;
                source.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                source.DecodePixelWidth = SmallWidth * 2;
                source.StreamSource = ms;
                source.EndInit();
            }

            source.Freeze();

            var radius = Math.Max(0, FilterRadius(blurRadiusAtDesignSize) * SmallScale);
            // Kenarların kararmaması için görseli hedef alandan büyük çiziyoruz
            var bleed = radius * 2 + SmallWidth * 0.02;

            var image = new Image
            {
                Source = source,
                Stretch = Stretch.UniformToFill,
                Width = SmallWidth + bleed * 2,
                Height = SmallHeight + bleed * 2,
            };
            if (radius > 0) image.Effect = Blur(radius);

            return Render(SmallWidth, SmallHeight, canvas => canvas.Add(image, -bleed, -bleed));
        }
        catch (Exception ex)
        {
            Log.Error($"Duvar kâğıdı işlenemedi: {path}", ex);
            return null;
        }
    }

    /// <summary>
    /// 1920x1080 arka planı oluşturur:
    ///  1. bulanık duvar kâğıdı + karartma katmanı (rgba(6,8,16,dim))
    ///  2. her kartın gölgesi (box-shadow: 0 24px 60px rgba(0,0,0,.28))
    ///  3. kartın altında kalan bölge: ek bulanıklık + %180 doygunluk (backdrop-filter: blur(30px) saturate(180%))
    /// Kartın yarı saydam rengi ve kenarlıkları WPF tarafından bunun üzerine çizilir.
    /// </summary>
    /// <param name="includeBase">false: zemin çizilmez, yalnızca gölgeler ve kart altları (masaüstü paneli için;
    /// kartların arası saydam kalır ve gerçek masaüstü görünür).</param>
    public static BitmapSource ComposeBackdrop(BitmapSource? blurred, IReadOnlyList<Rect> cards, double dimOpacity,
        double cardRadius = 28, bool includeBase = true)
    {
        var dimColor = Color.FromArgb((byte)Math.Clamp(dimOpacity * 255, 0, 255), 6, 8, 16);
        var fallback = Color.FromRgb(0x1C, 0x26, 0x45);

        // Karartılmış zemin (küçük boyutta) → doygunluğu artırılmış kopya
        var dimmedSmall = Render(SmallWidth, SmallHeight, c =>
        {
            c.Add(new Rectangle { Width = SmallWidth, Height = SmallHeight, Fill = new SolidColorBrush(fallback) }, 0, 0);
            if (blurred is not null)
                c.Add(new Image { Source = blurred, Width = SmallWidth, Height = SmallHeight, Stretch = Stretch.Fill }, 0, 0);
            c.Add(new Rectangle { Width = SmallWidth, Height = SmallHeight, Fill = new SolidColorBrush(dimColor) }, 0, 0);
        });
        var saturated = Saturate(dimmedSmall, 1.8);

        // Kart altı: ek 30px bulanıklık (küçük boyutta uygulanır)
        var backdropBlur = FilterRadius(30) * SmallScale;
        var cardBackdrop = Render(SmallWidth, SmallHeight, c =>
        {
            var bleed = backdropBlur * 3;
            c.Add(new Image
            {
                Source = saturated,
                Width = SmallWidth + bleed * 2,
                Height = SmallHeight + bleed * 2,
                Stretch = Stretch.Fill,
                Effect = Blur(backdropBlur),
            }, -bleed, -bleed);
        });

        return Render((int)DesignWidth, (int)DesignHeight, c =>
        {
            if (includeBase)
            {
                c.Add(new Image
                {
                    Source = dimmedSmall,
                    Width = DesignWidth,
                    Height = DesignHeight,
                    Stretch = Stretch.Fill,
                }, 0, 0);
            }

            // Gölgeler (kartın altında kalan kısmı bir sonraki adımda örtülür, CSS'teki gibi)
            foreach (var r in cards)
            {
                c.Add(new Border
                {
                    Width = r.Width,
                    Height = r.Height,
                    CornerRadius = new CornerRadius(cardRadius),
                    Background = new SolidColorBrush(Color.FromArgb((byte)(0.28 * 255), 0, 0, 0)),
                    Effect = Blur(ShadowRadius(60)),
                }, r.X, r.Y + 24);
            }

            foreach (var r in cards)
            {
                c.Add(new Image
                {
                    Source = cardBackdrop,
                    Width = DesignWidth,
                    Height = DesignHeight,
                    Stretch = Stretch.Fill,
                    Clip = new RectangleGeometry(r, cardRadius, cardRadius),
                }, 0, 0);
            }
        });
    }

    /// <summary>CSS filter: blur(N) → σ = N. WPF BlurEffect yarıçapı ≈ 3σ.</summary>
    private static double FilterRadius(double cssBlur) => cssBlur * 3;

    /// <summary>CSS box-shadow bulanıklığı N → σ = N/2. WPF yarıçapı ≈ 3σ.</summary>
    private static double ShadowRadius(double cssBlur) => cssBlur * 1.5;

    private static BlurEffect Blur(double radius) => new()
    {
        Radius = Math.Min(radius, 250),
        KernelType = KernelType.Gaussian,
        RenderingBias = RenderingBias.Quality,
    };

    /// <summary>CSS saturate() filtresinin renk matrisi.</summary>
    private static BitmapSource Saturate(BitmapSource source, double s)
    {
        var converted = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var w = converted.PixelWidth;
        var h = converted.PixelHeight;
        var stride = w * 4;
        var px = new byte[stride * h];
        converted.CopyPixels(px, stride, 0);

        double rr = 0.213 + 0.787 * s, rg = 0.715 - 0.715 * s, rb = 0.072 - 0.072 * s;
        double gr = 0.213 - 0.213 * s, gg = 0.715 + 0.285 * s, gb = 0.072 - 0.072 * s;
        double br = 0.213 - 0.213 * s, bg = 0.715 - 0.715 * s, bb = 0.072 + 0.928 * s;

        for (var i = 0; i < px.Length; i += 4)
        {
            double b = px[i], g = px[i + 1], r = px[i + 2];
            px[i] = Clamp(br * r + bg * g + bb * b);
            px[i + 1] = Clamp(gr * r + gg * g + gb * b);
            px[i + 2] = Clamp(rr * r + rg * g + rb * b);
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, px, stride);
        result.Freeze();
        return result;
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    private static BitmapSource Render(int width, int height, Action<Canvas> build)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true };
        build(canvas);
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(canvas);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// Yenileme yapıldıktan sonra çağrılır: o anki duvar kâğıdı durumunu "uygulandı" olarak işaretler
    /// ve olay üretmeyen değişiklikler için periyodik kontrolü başlatır.
    /// </summary>
    public void Watch(string? path)
    {
        _signature = DesktopWallpaper.Signature();
        _watchTimer.Start();
    }

    private void CheckForChange()
    {
        if (_disposed) return;
        var current = DesktopWallpaper.Signature();
        if (current == _signature) return;
        _signature = current;
        Log.Info("Duvar kâğıdı değişti, yenileniyor");
        Raise();
    }

    public void Dispose()
    {
        _disposed = true;
        _watchTimer.Stop();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}

internal static class CanvasExtensions
{
    public static void Add(this Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }
}
