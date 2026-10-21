using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PcDashboard.Core;

namespace PcDashboard.Services;

/// <summary>Windows'un "Tüm uygulamalar" listesindeki bir uygulama.</summary>
/// <param name="Name">Görünen ad ("Visual Studio Code").</param>
/// <param name="Target">shell:AppsFolder\... biçiminde başlatma adresi.</param>
public sealed record InstalledApp(string Name, string Target);

/// <summary>
/// Uygulamaları bulur, ikonlarını çıkarır ve başlatır.
///
/// Kaynak, Başlat menüsündeki "Tüm uygulamalar" listesiyle aynı olan shell:AppsFolder'dır. Hem klasik
/// programları (VS Code, Chrome…) hem Microsoft Store uygulamalarını (WhatsApp…) içerir; her uygulama
/// "shell:AppsFolder\&lt;kimlik&gt;" ile başlatılabilir. Masaüstündeki kısayollar da ayrıca taranır.
/// </summary>
public static class AppCatalog
{
    private const string AppsFolder = "shell:AppsFolder";
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);
    private static readonly string[] JunkWords = { "uninstall", "kaldır", "readme", "website", "help", "documentation", "release notes" };

    private static List<InstalledApp>? _cache;
    private static DateTime _cachedAt;

    /// <summary>Kurulu uygulamalar (STA iş parçacığında listelenir, 10 dk önbelleklenir).</summary>
    public static Task<List<InstalledApp>> GetInstalledAsync(bool refresh = false)
    {
        if (!refresh && _cache is not null && DateTime.UtcNow - _cachedAt < CacheTime)
            return Task.FromResult(_cache);

        var tcs = new TaskCompletionSource<List<InstalledApp>>();
        var thread = new Thread(() =>
        {
            try
            {
                var list = EnumerateAppsFolder();
                list.AddRange(EnumerateDesktopShortcuts());
                var result = list
                    .Where(a => !JunkWords.Any(j => a.Name.Contains(j, StringComparison.OrdinalIgnoreCase)))
                    .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _cache = result;
                _cachedAt = DateTime.UtcNow;
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                Log.Error("Uygulama listesi alınamadı", ex);
                tcs.SetResult(_cache ?? new List<InstalledApp>());
            }
        })
        { IsBackground = true, Name = "AppCatalog" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static List<InstalledApp> EnumerateAppsFolder()
    {
        var list = new List<InstalledApp>();
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) return list;

        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic folder = shell.NameSpace(AppsFolder);
            dynamic items = folder.Items();
            int count = items.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                if (item is null) continue;
                string name = item.Name;
                string path = item.Path; // AUMID veya .exe/.lnk yolu
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                    list.Add(new InstalledApp(name.Trim(), $@"{AppsFolder}\{path}"));
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }

        return list;
    }

    private static IEnumerable<InstalledApp> EnumerateDesktopShortcuts()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        foreach (var folder in folders.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.*")
                    .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
                yield return new InstalledApp(System.IO.Path.GetFileNameWithoutExtension(file), file);
        }
    }

    /// <summary>
    /// Bir başlatıcı girdisinin başlatma adresini bulur. Path varsa o; yoksa Find'daki adlar sırayla
    /// önce tam eşleşme, sonra "… ile başlayan", sonra "… içeren" olarak aranır.
    /// </summary>
    public static string? Resolve(AppEntry entry, IReadOnlyList<InstalledApp> installed)
    {
        if (!string.IsNullOrWhiteSpace(entry.Path))
        {
            var p = Environment.ExpandEnvironmentVariables(entry.Path.Trim());
            if (p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || File.Exists(p)) return p;
        }

        var terms = entry.Find.Count > 0 ? entry.Find : new List<string> { entry.Name };
        foreach (var term in terms.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            var t = term.Trim();
            var hit = installed.FirstOrDefault(a => a.Name.Equals(t, StringComparison.OrdinalIgnoreCase))
                      ?? installed.FirstOrDefault(a => a.Name.StartsWith(t, StringComparison.OrdinalIgnoreCase))
                      ?? installed.FirstOrDefault(a => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Target;
        }

        return null;
    }

    public static bool Launch(string target, string? arguments = null)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(arguments)) psi.Arguments = arguments;
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Uygulama başlatılamadı: {target}", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // İkon: Gezgin'in kullandığı IShellItemImageFactory (Store uygulamaları dahil, saydamlık korunur)
    // UI (STA) iş parçacığında çağrılmalı.
    // ------------------------------------------------------------------

    public static ImageSource? GetIcon(string target, int size = 96)
    {
        IShellItemImageFactory? factory = null;
        var hBitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out factory);
            if (hr != 0 || factory is null) return null;

            hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.BiggerSizeOk | SIIGBF.IconOnly, out hBitmap);
            if (hr != 0 || hBitmap == IntPtr.Zero)
            {
                hr = factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF.BiggerSizeOk, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero) return null;
            }

            return ToBitmapSource(hBitmap);
        }
        catch (Exception ex)
        {
            Log.Warn($"İkon alınamadı ({target}): {ex.Message}");
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>32 bit DIB'i saydamlığı koruyarak kopyalar (CreateBitmapSourceFromHBitmap alfayı kaybeder).</summary>
    private static BitmapSource? ToBitmapSource(IntPtr hBitmap)
    {
        if (GetObject(hBitmap, Marshal.SizeOf<DIBSECTION>(), out var ds) == 0) return null;
        var bm = ds.dsBm;

        if (bm.bmBits == IntPtr.Zero || bm.bmBitsPixel != 32)
        {
            var fallback = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        var height = Math.Abs(bm.bmHeight);
        var stride = bm.bmWidthBytes;
        var pixels = new byte[stride * height];
        Marshal.Copy(bm.bmBits, pixels, 0, pixels.Length);

        BitmapSource source = BitmapSource.Create(bm.bmWidth, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);

        // Alttan yukarı saklanmış DIB'i çevir
        if (ds.dsBmih.biHeight > 0)
            source = new TransformedBitmap(source, new ScaleTransform(1, -1));

        source.Freeze();
        return source;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0;
        public uint dsBitfields1;
        public uint dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hObject, int size, out DIBSECTION ds);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
