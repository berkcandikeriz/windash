using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PcDashboard.Controls;
using PcDashboard.Core;
using PcDashboard.Interop;
using PcDashboard.Services;

namespace PcDashboard;

/// <summary>
/// "Masaüstüne göm" modundaki panel. Seçilen kartları masaüstünün sağ kenarında gösterir:
///  - Pencerelerin arkasında, duvar kâğıdının (ve masaüstü simgelerinin) üzerinde durur; öne gelmez, odak çalmaz.
///  - Görev çubuğunda ve Alt+Tab'da görünmez; Win+D (masaüstünü göster) ile kaybolmaz.
///  - Genişliği her çözünürlükte monitör genişliğinin %20'si (Desktop.WidthPercent), yüksekliği tam.
///  - Kartlar Düzenle modunda taşınıp boyutlandırılır; yerleşim state.json'a kaydedilir.
///  - Kartlar sade ("compact") görünümdedir.
/// </summary>
public partial class DesktopWindow : Window
{
    private readonly List<WidgetViewModel> _widgets = new();
    private readonly List<FrameworkElement> _cards = new();
    private readonly WallpaperService _wallpaper = new();
    private AppSettings _settings;
    /// <summary>Monitörde görünen duvar kâğıdı, fiziksel pikselin yarısı ölçeğinde (bulanıklaştırma kaynağı).</summary>
    private BitmapSource? _displayed;
    private const double BackdropScale = 0.5;
    private IntPtr _hwnd;
    private MonitorInfo? _monitor;
    private Int32Rect _bounds;
    private double _scale = 1;
    private bool _loading;
    private int _rows = 1;
    private LayoutEditor? _editor;
    private System.Windows.Controls.Primitives.Popup? _editBar;
    private readonly DispatcherTimer _editButtonTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };

    /// <summary>Panelin tasarım genişliği; kartların iç tasarımı bu genişliğe göre.</summary>
    private const double DesignWidth = 400;
    private const double GridMargin = 12;

    public bool IsEditing => _editor is not null;

    /// <summary>Uygulama paneli kendisi kapatıyor (true) ya da Explorer / Windows kapattı (false).</summary>
    public bool ClosingByApp { get; set; }

    public DesktopWindow()
    {
        InitializeComponent();
        _settings = SettingsLoader.LoadSettings();

        SourceInitialized += OnSourceInitialized;
        Loaded += async (_, _) => await LoadAsync(reloadSettings: false);
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            Position();
            ComposeBackdrop();
        }, DispatcherPriority.Background);
        Closed += OnClosed;

        // Düzenle düğmesi: fare kartların üstündeyken görünür
        _editButtonTimer.Tick += (_, _) =>
        {
            _editButtonTimer.Stop();
            if (!EditButton.IsMouseOver) EditButton.Visibility = Visibility.Collapsed;
        };
        WidgetGrid.MouseEnter += (_, _) => ShowEditButton();
        WidgetGrid.MouseLeave += (_, _) => _editButtonTimer.Start();
        EditButton.MouseEnter += (_, _) => _editButtonTimer.Stop();
        EditButton.MouseLeave += (_, _) => _editButtonTimer.Start();

        // Duvar kâğıdı değişince (Ayarlar, slayt gösterisi, Spotlight, başka uygulama) kendiliğinden yenilenir
        _wallpaper.Changed += (_, _) => ApplyWallpaper();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public Task ReloadAsync() => IsLoaded ? LoadAsync(reloadSettings: true) : Task.CompletedTask;

    private async Task LoadAsync(bool reloadSettings)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (reloadSettings) _settings = SettingsLoader.LoadSettings();

            EndEdit(keep: false);
            ApplyCardTint();
            DisposeWidgets();
            ComputeGeometry();
            BuildGrid();
            Position();
            ApplyWallpaper();

            foreach (var vm in _widgets.ToList())
            {
                try
                {
                    await vm.StartAsync();
                }
                catch (Exception ex)
                {
                    Log.Error($"Masaüstü: {vm.GetType().Name} başlatılamadı", ex);
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    // ---------- Kartlar ----------

    /// <summary>Kaydedilmiş (Düzenle) yerleşim, yoksa ayarlardaki varsayılan — kopya olarak.</summary>
    private List<WidgetPlacement> Placements()
    {
        var source = AppState.Current.DesktopLayout is { Count: > 0 } saved ? saved : _settings.Desktop.Widgets;
        return source
            .Where(p => !string.IsNullOrWhiteSpace(p.Type) && !p.Type.Trim().Equals("window", StringComparison.OrdinalIgnoreCase))
            .Select(p => new WidgetPlacement
            {
                Type = p.Type,
                Column = p.Column,
                Row = p.Row,
                ColumnSpan = p.ColumnSpan,
                RowSpan = p.RowSpan,
                Variant = p.Variant,
            })
            .ToList();
    }

    private void BuildGrid()
    {
        WidgetGrid.Children.Clear();
        WidgetGrid.RowDefinitions.Clear();
        WidgetGrid.ColumnDefinitions.Clear();
        _cards.Clear();

        var columns = Math.Clamp(_settings.Desktop.Columns, 1, 12);
        var rows = Math.Max(1, _rows);

        for (var c = 0; c < columns; c++)
            WidgetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows; r++)
            WidgetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        foreach (var placement in Placements())
        {
            WidgetInstance? instance;
            try
            {
                instance = WidgetFactory.Create(placement, _settings);
            }
            catch (Exception ex)
            {
                Log.Error($"Masaüstü: '{placement.Type}' kartı oluşturulamadı", ex);
                continue;
            }

            if (instance is null)
            {
                Log.Warn($"Masaüstü: bilinmeyen kart türü '{placement.Type}'");
                continue;
            }

            instance.View.DataContext = instance.ViewModel;
            var card = new ContentControl
            {
                Style = (Style)FindResource("GlassCard"),
                Margin = new Thickness(5),
                Padding = instance.Padding,
                // Kart küçültülürse içerik kırpılmaz, orantılı küçülür
                Content = new FitBox { Child = instance.View },
                Tag = placement,
            };

            // Ekran kısaysa (daha az satır) kartı sığdır
            var rowSpan = Math.Clamp(placement.RowSpan, 1, rows);
            var row = Math.Clamp(placement.Row, 0, rows - rowSpan);
            var colSpan = Math.Clamp(placement.ColumnSpan, 1, columns);
            var col = Math.Clamp(placement.Column, 0, columns - colSpan);
            Grid.SetColumn(card, col);
            Grid.SetRow(card, row);
            Grid.SetColumnSpan(card, colSpan);
            Grid.SetRowSpan(card, rowSpan);

            WidgetGrid.Children.Add(card);
            _cards.Add(card);
            _widgets.Add(instance.ViewModel);
        }
    }

    private void DisposeWidgets()
    {
        foreach (var vm in _widgets) vm.Dispose();
        _widgets.Clear();
    }

    // ---------- Konum ----------

    private MonitorInfo? PickMonitor()
    {
        var monitors = MonitorService.GetMonitors();
        if (monitors.Count == 0) return null;

        var name = _settings.Desktop.MonitorDeviceName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var named = monitors.FirstOrDefault(m => m.DeviceName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (named is not null) return named;
            Log.Warn($"Masaüstü: '{name}' adlı monitör bulunamadı, ana ekran kullanılıyor.");
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Panelin boyutu: genişlik = monitör genişliğinin %20'si (her çözünürlükte), yükseklik = görev çubuğu hariç tam.
    /// Tasarım genişliği sabit 400 birim; tasarım yüksekliği ve satır sayısı ekranın oranına göre değişir.
    /// </summary>
    private void ComputeGeometry()
    {
        _monitor = PickMonitor();
        if (_monitor is null) return;

        var work = _monitor.Work;
        var percent = Math.Clamp(_settings.Desktop.WidthPercent, 10, 60) / 100.0;
        var width = (int)Math.Round(Math.Min(work.Width, _monitor.Width * percent));
        var height = work.Height;

        _scale = width / DesignWidth;
        _bounds = new Int32Rect(work.X + work.Width - width, work.Y, width, height);

        DesignRoot.Width = DesignWidth;
        DesignRoot.Height = height / _scale;
        WidgetGrid.Margin = new Thickness(GridMargin);

        var rowHeight = Math.Clamp(_settings.Desktop.RowHeight, 8, 200);
        _rows = Math.Max(1, (int)Math.Floor((DesignRoot.Height - GridMargin * 2) / rowHeight));
    }

    /// <summary>Paneli monitörün kullanılabilir alanının sağ kenarına yerleştirir (fiziksel piksel).</summary>
    private void Position()
    {
        if (_hwnd == IntPtr.Zero) return;
        var rows = _rows;
        ComputeGeometry();
        if (_monitor is null) return;

        // Ekran yüksekliği değişti: satır sayısı değiştiyse kartları yeniden diz
        if (rows != _rows && IsLoaded && !_loading) Dispatcher.InvokeAsync(() => _ = ReloadAsync());

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height,
            NativeMethods.SWP_NOACTIVATE);
    }

    // ---------- Düzenle ----------

    private void ShowEditButton()
    {
        _editButtonTimer.Stop();
        if (!IsEditing) EditButton.Visibility = Visibility.Visible;
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => BeginEdit();

    /// <summary>Kartları taşıma / boyutlandırma modu.</summary>
    public void BeginEdit()
    {
        if (IsEditing || !IsLoaded) return;
        EditButton.Visibility = Visibility.Collapsed;
        _editor = new LayoutEditor(WidgetGrid, _cards, ComposeBackdrop);
        _editor.Begin();
        _editBar = EditToolbar.Show(DesignRoot, "Sürükle: taşı · köşe: boyutlandır",
            done: () => EndEdit(keep: true),
            cancel: () => EndEdit(keep: false),
            reset: ResetLayout);
    }

    private void EndEdit(bool keep)
    {
        if (_editor is null) return;
        _editor.End(keep);
        _editor = null;
        if (_editBar is not null) _editBar.IsOpen = false;
        _editBar = null;

        if (keep)
        {
            AppState.Current.DesktopLayout = _cards.Select(c => c.Tag).OfType<WidgetPlacement>().ToList();
            AppState.Current.Save();
            Log.Info("Masaüstü yerleşimi kaydedildi");
        }

        ComposeBackdrop();
    }

    private void ResetLayout()
    {
        EndEdit(keep: false);
        AppState.Current.DesktopLayout = null;
        AppState.Current.Save();
        _ = ReloadAsync();
    }

    // ---------- Cam arka plan ----------

    /// <summary>Kartın yarı saydam rengi (masaüstünde ayrı: Desktop.CardTint).</summary>
    private void ApplyCardTint()
    {
        Color tint;
        try
        {
            tint = (Color)ColorConverter.ConvertFromString(_settings.Desktop.CardTint);
        }
        catch
        {
            tint = Color.FromArgb(0x1F, 0, 0, 0);
        }

        var brush = new SolidColorBrush(tint);
        brush.Freeze();
        Resources["CardBackgroundBrush"] = brush;
    }

    /// <summary>Monitörde o an görünen duvar kâğıdını birebir üretir (resim, yerleştirme biçimi, kenar rengi).</summary>
    private void ApplyWallpaper()
    {
        try
        {
            _monitor ??= PickMonitor();
            if (_monitor is null) return;

            var path = _settings.Wallpaper.OverridePath;
            _displayed = !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? WallpaperService.CreateBlurred(path, 0)
                : DesktopWallpaper.Render(_monitor, MonitorService.GetMonitors(), BackdropScale);
        }
        catch (Exception ex)
        {
            Log.Error("Masaüstü duvar kâğıdı okunamadı", ex);
            _displayed = null;
        }

        _wallpaper.Watch(null);
        Dispatcher.InvokeAsync(ComposeBackdrop, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Kartların altına duvar kâğıdının tam o bölgesinin yalnızca bulanık halini koyar.
    /// Renk, doygunluk ve parlaklık değiştirilmez; üstüne yalnızca kartın ince rengi (Desktop.CardTint) gelir.
    /// </summary>
    private void ComposeBackdrop()
    {
        try
        {
            if (_monitor is null || _scale <= 0 || _displayed is null)
            {
                BackdropImage.Source = null;
                return;
            }

            DesignRoot.UpdateLayout();

            // Panel tasarım birimi → monitörün fiziksel pikseli → üretilen görüntünün pikseli
            var k = _displayed.PixelWidth / (double)_monitor.Width;
            double offsetX = _bounds.X - _monitor.X;
            double offsetY = _bounds.Y - _monitor.Y;

            var rects = new List<Rect>();
            foreach (var card in _cards)
            {
                if (card.ActualWidth <= 0 || card.ActualHeight <= 0) continue;
                var b = card.TransformToAncestor(DesignRoot)
                    .TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                rects.Add(new Rect(
                    (offsetX + b.X * _scale) * k,
                    (offsetY + b.Y * _scale) * k,
                    b.Width * _scale * k,
                    b.Height * _scale * k));
            }

            // CSS blur(N) → σ = N tasarım pikseli; WPF yarıçapı ≈ 3σ
            var unit = _scale * k;
            BackdropImage.Source = DesktopWallpaper.ComposeGlass(_displayed, rects,
                blurRadius: Math.Max(0, _settings.Desktop.BlurRadius) * 3 * unit,
                cornerRadius: 28 * unit,
                shadowOffset: 18 * unit,
                shadowRadius: 48 * 1.5 * unit);

            // Monitörün tamamı için üretilen görüntüyü panelin olduğu yere hizala
            BackdropImage.Width = _monitor.Width / _scale;
            BackdropImage.Height = _monitor.Height / _scale;
            Canvas.SetLeft(BackdropImage, -offsetX / _scale);
            Canvas.SetTop(BackdropImage, -offsetY / _scale);
        }
        catch (Exception ex)
        {
            Log.Error("Masaüstü paneli arka planı oluşturulamadı", ex);
        }
    }

    // ---------- Masaüstüne yerleşme ----------

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Görev çubuğunda / Alt+Tab'da görünme, tıklanınca odak alma
        var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex = (ex | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

        // Sahibi masaüstü (Progman) olsun: Win+D ile gizlenmez, her zaman masaüstünün hemen üstünde kalır
        var progman = NativeMethods.FindWindow("Progman", null);
        if (progman != IntPtr.Zero) NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWLP_HWNDPARENT, progman);
        else Log.Warn("Masaüstü penceresi (Progman) bulunamadı; panel normal pencere gibi davranacak.");

        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        Position();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_WINDOWPOSCHANGING:
            {
                // Tıklansa da öne gelmesin: her z-sırası değişikliğini en alta çevir
                var pos = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                if ((pos.flags & NativeMethods.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, lParam, false);
                }

                break;
            }

            case NativeMethods.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(NativeMethods.MA_NOACTIVATE);

            case NativeMethods.WM_SYSCOMMAND:
                // Alt+F4 vb. ile panel kapanmasın (kapatmak için tepsi menüsü > Masaüstüne göm)
                if ((wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_CLOSE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }

                break;

            case NativeMethods.WM_CLOSE:
                // Dışarıdan kapatma isteği (ör. güncelleme kurulumu): uygulamadan çık
                if (!ClosingByApp && !App.Current.IsExiting)
                {
                    handled = true;
                    App.Current.BeginExit();
                    return IntPtr.Zero;
                }

                break;
        }

        return IntPtr.Zero;
    }

    // ---------- Olaylar ----------

    /// <summary>Çözünürlük / monitör değişti: yeniden yerleş ve o monitörün duvar kâğıdını yeniden üret.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            Position();
            ApplyWallpaper();
        }, DispatcherPriority.Background);

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _wallpaper.Dispose();
        DisposeWidgets();
    }
}
