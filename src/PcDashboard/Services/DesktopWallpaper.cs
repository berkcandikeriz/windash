using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>Windows'un duvar kâğıdını ekrana yerleştirme biçimi (Ayarlar > Kişiselleştirme > Arka plan).</summary>
public enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>Bir monitörde o an görünen duvar kâğıdı.</summary>
public sealed record DisplayedWallpaper(string? Path, WallpaperFit Fit, Color Background);

/// <summary>
/// Masaüstünde gerçekte görünen duvar kâğıdını piksel piksel yeniden üretir (monitöre özel resim, yerleştirme biçimi,
/// kenar rengi) ve masaüstü panelinin camı için yalnızca bulanıklaştırır: renkler, doygunluk ve parlaklık değişmez.
/// </summary>
public static class DesktopWallpaper
{
    // ---------- Hangi resim, nasıl yerleştirilmiş ----------

    public static DisplayedWallpaper Get(MonitorInfo monitor)
    {
        string? path = null;
        var fit = WallpaperFit.Fill;
        var background = Colors.Black;

        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                fit = (WallpaperFit)wallpaper.GetPosition();
                var bgr = wallpaper.GetBackgroundColor();
                background = Color.FromRgb((byte)(bgr & 0xFF), (byte)((bgr >> 8) & 0xFF), (byte)((bgr >> 16) & 0xFF));

                var count = wallpaper.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    var id = wallpaper.GetMonitorDevicePathAt(i);
                    var r = wallpaper.GetMonitorRECT(id);
                    if (r.Left != monitor.X || r.Top != monitor.Y || r.Right - r.Left != monitor.Width) continue;

                    var p = wallpaper.GetWallpaper(id);
                    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) path = p;
                    break;
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(wallpaper);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Monitörün duvar kâğıdı okunamadı: {ex.Message}");
        }

        // Slayt gösterisi / Spotlight / tek resim: Windows'un o an kullandığı kopya
        path ??= WallpaperService.GetCurrentWallpaperPath();
        if (!Enum.IsDefined(fit)) fit = WallpaperFit.Fill;
        return new DisplayedWallpaper(path, fit, background);
    }

    /// <summary>Değişiklik takibi için: resimler, dosya tarihleri ve yerleştirme biçimi.</summary>
    public static string Signature()
    {
        var parts = new List<string>();
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                parts.Add(wallpaper.GetPosition().ToString());
                parts.Add(wallpaper.GetBackgroundColor().ToString());
                var count = wallpaper.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        parts.Add(FileStamp(wallpaper.GetWallpaper(wallpaper.GetMonitorDevicePathAt(i))));
                    }
                    catch
                    {
                        parts.Add("?");
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(wallpaper);
            }
        }
        catch
        {
            // IDesktopWallpaper yoksa yalnızca aşağıdakiler
        }

        parts.Add(FileStamp(WallpaperService.GetCurrentWallpaperPath()));
        parts.Add(FileStamp(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Themes", "TranscodedWallpaper")));
        return string.Join("|", parts);
    }

    private static string FileStamp(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "-";
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{path}:{info.Length}:{info.LastWriteTimeUtc.Ticks}" : path;
        }
        catch
        {
            return path;
        }
    }

    // ---------- Görünen duvar kâğıdını üret ----------

    /// <summary>
    /// Monitörde görünen duvar kâğıdını, monitörün fiziksel piksel boyutunun <paramref name="scale"/> katı
    /// büyüklüğünde üretir (yerleştirme biçimi ve kenar rengi dahil). Renkler değiştirilmez.
    /// </summary>
    public static BitmapSource? Render(MonitorInfo monitor, IReadOnlyList<MonitorInfo> allMonitors, double scale)
    {
        var info = Get(monitor);
        var width = Math.Max(1, (int)Math.Round(monitor.Width * scale));
        var height = Math.Max(1, (int)Math.Round(monitor.Height * scale));

        BitmapSource? image = null;
        if (info.Path is not null)
        {
            try
            {
                image = Load(info.Path);
            }
            catch (Exception ex)
            {
                Log.Error($"Duvar kâğıdı okunamadı: {info.Path}", ex);
            }
        }

        return RenderCanvas(width, height, canvas =>
        {
            canvas.Add(new Rectangle { Width = width, Height = height, Fill = new SolidColorBrush(info.Background) }, 0, 0);
            if (image is null) return;

            // Resmin doğal boyutu (fiziksel piksel) → bu çıktının ölçeğinde
            double iw = image.PixelWidth * scale, ih = image.PixelHeight * scale;

            switch (info.Fit)
            {
                case WallpaperFit.Stretch:
                    canvas.Add(Img(image, width, height), 0, 0);
                    break;

                case WallpaperFit.Fit:
                {
                    var k = Math.Min(width / iw, height / ih);
                    canvas.Add(Img(image, iw * k, ih * k), (width - iw * k) / 2, (height - ih * k) / 2);
                    break;
                }

                case WallpaperFit.Center:
                    canvas.Add(Img(image, iw, ih), (width - iw) / 2, (height - ih) / 2);
                    break;

                case WallpaperFit.Tile:
                    canvas.Add(new Rectangle
                    {
                        Width = width,
                        Height = height,
                        Fill = new ImageBrush(image)
                        {
                            TileMode = TileMode.Tile,
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, iw, ih),
                            Stretch = Stretch.Fill,
                        },
                    }, 0, 0);
                    break;

                case WallpaperFit.Span:
                {
                    // Tüm monitörleri kaplayan tek resim: sanal ekranı doldur, bu monitörün payını kes
                    var left = allMonitors.Min(m => m.X);
                    var top = allMonitors.Min(m => m.Y);
                    var right = allMonitors.Max(m => m.X + m.Width);
                    var bottom = allMonitors.Max(m => m.Y + m.Height);
                    double vw = (right - left) * scale, vh = (bottom - top) * scale;
                    var k = Math.Max(vw / iw, vh / ih);
                    var x = (vw - iw * k) / 2 - (monitor.X - left) * scale;
                    var y = (vh - ih * k) / 2 - (monitor.Y - top) * scale;
                    canvas.Add(Img(image, iw * k, ih * k), x, y);
                    break;
                }

                default: // Fill
                {
                    var k = Math.Max(width / iw, height / ih);
                    canvas.Add(Img(image, iw * k, ih * k), (width - iw * k) / 2, (height - ih * k) / 2);
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Masaüstü panelinin cam arka planı: <paramref name="displayed"/> görüntüsünün yalnızca bulanık hali,
    /// kartların altında (yuvarlak köşeli) ve kart gölgeleriyle. Kartların dışı saydamdır.
    /// </summary>
    /// <param name="cards">Kartlar, <paramref name="displayed"/> görüntüsünün piksel koordinatlarında.</param>
    /// <param name="blurRadius">Bulanıklık yarıçapı (görüntü pikseli, WPF BlurEffect).</param>
    public static BitmapSource ComposeGlass(BitmapSource displayed, IReadOnlyList<Rect> cards, double blurRadius,
        double cornerRadius, double shadowOffset, double shadowRadius)
    {
        var width = displayed.PixelWidth;
        var height = displayed.PixelHeight;
        var blurred = Blur(displayed, blurRadius);

        return RenderCanvas(width, height, c =>
        {
            foreach (var r in cards)
            {
                c.Add(new Border
                {
                    Width = r.Width,
                    Height = r.Height,
                    CornerRadius = new CornerRadius(cornerRadius),
                    Background = new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)),
                    Effect = new BlurEffect { Radius = Math.Min(shadowRadius, 200), KernelType = KernelType.Gaussian },
                }, r.X, r.Y + shadowOffset);
            }

            foreach (var r in cards)
            {
                c.Add(new Image
                {
                    Source = blurred,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Fill,
                    Clip = new RectangleGeometry(r, cornerRadius, cornerRadius),
                }, 0, 0);
            }
        });
    }

    /// <summary>
    /// Gauss bulanıklığı. Kenarlarda kararma olmasın diye görüntü, kenar pikselleri uzatılarak genişletilir
    /// (panel ekranın sağ kenarına dayalı olduğu için önemli).
    /// </summary>
    private static BitmapSource Blur(BitmapSource source, double radius)
    {
        var w = source.PixelWidth;
        var h = source.PixelHeight;
        radius = Math.Clamp(radius, 0, 250);
        if (radius < 0.5) return source;

        var pad = (int)Math.Ceiling(radius) + 2;
        var full = RenderCanvas(w + pad * 2, h + pad * 2, outer =>
        {
            var inner = new Canvas { Width = w + pad * 2, Height = h + pad * 2 };
            inner.Add(Img(source, w, h), pad, pad);

            // Kenar uzatma: 1 piksellik şeritler ve köşe pikselleri
            inner.Add(Img(Crop(source, 0, 0, 1, h), pad, h), 0, pad);
            inner.Add(Img(Crop(source, w - 1, 0, 1, h), pad, h), pad + w, pad);
            inner.Add(Img(Crop(source, 0, 0, w, 1), w, pad), pad, 0);
            inner.Add(Img(Crop(source, 0, h - 1, w, 1), w, pad), pad, pad + h);
            inner.Add(Img(Crop(source, 0, 0, 1, 1), pad, pad), 0, 0);
            inner.Add(Img(Crop(source, w - 1, 0, 1, 1), pad, pad), pad + w, 0);
            inner.Add(Img(Crop(source, 0, h - 1, 1, 1), pad, pad), 0, pad + h);
            inner.Add(Img(Crop(source, w - 1, h - 1, 1, 1), pad, pad), pad + w, pad + h);

            inner.Effect = new BlurEffect
            {
                Radius = radius,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality,
            };
            outer.Add(inner, 0, 0);
        });

        var cropped = new CroppedBitmap(full, new Int32Rect(pad, pad, w, h));
        cropped.Freeze();
        return cropped;
    }

    // ---------- Yardımcılar ----------

    private static BitmapSource Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bitmap = new BitmapImage();
        using (var ms = new MemoryStream(bytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
        }

        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource Crop(BitmapSource source, int x, int y, int w, int h)
    {
        var crop = new CroppedBitmap(source, new Int32Rect(x, y, Math.Max(1, w), Math.Max(1, h)));
        crop.Freeze();
        return crop;
    }

    private static Image Img(ImageSource source, double width, double height)
    {
        var image = new Image { Source = source, Width = width, Height = height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static BitmapSource RenderCanvas(int width, int height, Action<Canvas> build)
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

    // ---------- IDesktopWallpaper (Windows 8+) ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId);

        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetMonitorDevicePathAt(uint monitorIndex);

        uint GetMonitorDevicePathCount();

        Rect32 GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);

        void SetBackgroundColor(uint color);

        uint GetBackgroundColor();

        void SetPosition(int position);

        int GetPosition();
    }

    [ComImport]
    [Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass
    {
    }
}
