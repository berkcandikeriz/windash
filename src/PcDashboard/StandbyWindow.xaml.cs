using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PcDashboard.Core;
using PcDashboard.Interop;
using PcDashboard.Services;

namespace PcDashboard;

/// <summary>
/// Bekleme ekranı penceresi. Bir monitörü tamamen kaplar, her şeyin üstünde durur.
/// <paramref name="showDashboard"/> true ise ana paneldeki yerleşimi (layout.json) gösterir, değilse siyah kalır.
/// </summary>
public partial class StandbyWindow : Window
{
    private readonly MonitorInfo _monitor;
    private readonly bool _showDashboard;
    private readonly List<WidgetViewModel> _widgets = new();
    private readonly List<FrameworkElement> _cards = new();
    private readonly AppSettings _settings;
    private BitmapSource? _blurred;

    public StandbyWindow(MonitorInfo monitor, bool showDashboard)
    {
        InitializeComponent();
        _monitor = monitor;
        _showDashboard = showDashboard;
        _settings = SettingsLoader.LoadSettings();

        if (!showDashboard) Root.Visibility = Visibility.Collapsed;

        SourceInitialized += (_, _) => Place();
        DpiChanged += (_, _) => Dispatcher.InvokeAsync(Place, DispatcherPriority.Background);
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) =>
        {
            foreach (var vm in _widgets) vm.Dispose();
            _widgets.Clear();
        };
    }

    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _monitor.X, _monitor.Y, _monitor.Width, _monitor.Height,
            NativeMethods.SWP_FRAMECHANGED);
    }

    private async Task LoadAsync()
    {
        if (!_showDashboard) return;

        try
        {
            DimLayer.Opacity = Math.Clamp(_settings.Wallpaper.DimOpacity, 0, 1);
            BuildGrid(SettingsLoader.LoadLayout());
            ApplyWallpaper();

            foreach (var vm in _widgets.ToList())
            {
                try
                {
                    await vm.StartAsync();
                }
                catch (Exception ex)
                {
                    Log.Error($"Bekleme ekranı: {vm.GetType().Name} başlatılamadı", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Bekleme ekranı yüklenemedi", ex);
        }
    }

    private void BuildGrid(LayoutConfig layout)
    {
        var columns = Math.Max(1, layout.Columns);
        var rows = Math.Max(1, layout.Rows);
        for (var c = 0; c < columns; c++)
            WidgetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows; r++)
            WidgetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        foreach (var placement in layout.Widgets)
        {
            // Pencere düğmeleri bekleme ekranında anlamsız
            if (placement.Type.Trim().Equals("window", StringComparison.OrdinalIgnoreCase)) continue;

            WidgetInstance? instance;
            try
            {
                instance = WidgetFactory.Create(placement, _settings);
            }
            catch (Exception ex)
            {
                Log.Error($"Bekleme ekranı: '{placement.Type}' kartı oluşturulamadı", ex);
                continue;
            }

            if (instance is null) continue;

            instance.View.DataContext = instance.ViewModel;
            var card = new ContentControl
            {
                Style = (Style)FindResource("GlassCard"),
                Padding = instance.Padding,
                Content = instance.View,
            };

            Grid.SetColumn(card, Math.Clamp(placement.Column, 0, columns - 1));
            Grid.SetRow(card, Math.Clamp(placement.Row, 0, rows - 1));
            Grid.SetColumnSpan(card, Math.Max(1, placement.ColumnSpan));
            Grid.SetRowSpan(card, Math.Max(1, placement.RowSpan));

            WidgetGrid.Children.Add(card);
            _cards.Add(card);
            _widgets.Add(instance.ViewModel);
        }
    }

    private void ApplyWallpaper()
    {
        var w = _settings.Wallpaper;
        var path = !string.IsNullOrWhiteSpace(w.OverridePath) && File.Exists(w.OverridePath)
            ? w.OverridePath
            : WallpaperService.GetCurrentWallpaperPath();

        _blurred = path is null ? null : WallpaperService.CreateBlurred(path, w.BlurRadius);
        WallpaperImage.Source = _blurred;
        Dispatcher.InvokeAsync(ComposeBackdrop, DispatcherPriority.Loaded);
    }

    private void ComposeBackdrop()
    {
        try
        {
            DesignRoot.UpdateLayout();
            var rects = new List<Rect>();
            foreach (var card in _cards)
            {
                if (card.ActualWidth <= 0 || card.ActualHeight <= 0) continue;
                rects.Add(card.TransformToAncestor(DesignRoot)
                    .TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight)));
            }

            BackdropImage.Source = WallpaperService.ComposeBackdrop(_blurred, rects,
                Math.Clamp(_settings.Wallpaper.DimOpacity, 0, 1));
        }
        catch (Exception ex)
        {
            Log.Error("Bekleme ekranı arka planı oluşturulamadı", ex);
        }
    }
}
