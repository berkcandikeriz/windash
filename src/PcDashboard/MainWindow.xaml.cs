using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard;

public partial class MainWindow : Window
{
    private readonly List<WidgetViewModel> _widgets = new();
    private readonly List<FrameworkElement> _cards = new();
    private readonly WallpaperService _wallpaper = new();
    private AppSettings _settings;
    private BitmapSource? _blurredWallpaper;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsLoader.LoadSettings();
        InitChrome();
        App.ApplyTheme(_settings);
        Topmost = _settings.Display.Topmost;

        SourceInitialized += (_, _) => ApplyMode();
        // Farklı ölçekli bir monitöre taşınınca Windows pencereyi yeniden boyutlandırır; tam ekran / büyütülmüşse yeniden yerleştir
        DpiChanged += (_, _) =>
        {
            if (_mode != DisplayMode.Windowed) Dispatcher.InvokeAsync(() => ApplyMode(), DispatcherPriority.Background);
        };
        Loaded += async (_, _) => await LoadAllAsync(reloadSettings: false);
        Closed += OnClosed;
        PreviewKeyDown += OnPreviewKeyDown;

        _wallpaper.Changed += (_, _) => ApplyWallpaper();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private async Task LoadAllAsync(bool reloadSettings)
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (reloadSettings)
            {
                _settings = SettingsLoader.LoadSettings();
                App.ApplyTheme(_settings);
                ApplyMode();
            }

            Autostart.Sync(_settings.StartWithWindows);
            AudioHub.Aliases = _settings.Devices.AudioAliases;

            DisposeWidgets();
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
                    Log.Error($"{vm.GetType().Name} başlatılamadı", ex);
                }
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void BuildGrid(LayoutConfig layout)
    {
        WidgetGrid.Children.Clear();
        WidgetGrid.RowDefinitions.Clear();
        WidgetGrid.ColumnDefinitions.Clear();
        _cards.Clear();

        var columns = Math.Max(1, layout.Columns);
        var rows = Math.Max(1, layout.Rows);
        for (var c = 0; c < columns; c++)
            WidgetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows; r++)
            WidgetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        foreach (var placement in layout.Widgets)
        {
            WidgetInstance? instance;
            try
            {
                instance = WidgetFactory.Create(placement, _settings);
            }
            catch (Exception ex)
            {
                Log.Error($"'{placement.Type}' widget'ı oluşturulamadı", ex);
                continue;
            }

            if (instance is null)
            {
                Log.Warn($"Bilinmeyen widget türü: '{placement.Type}'");
                continue;
            }

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

    // ---------- Arka plan ----------

    private void ApplyWallpaper()
    {
        var w = _settings.Wallpaper;
        DimLayer.Opacity = Math.Clamp(w.DimOpacity, 0, 1);

        var path = !string.IsNullOrWhiteSpace(w.OverridePath) && File.Exists(w.OverridePath)
            ? w.OverridePath
            : WallpaperService.GetCurrentWallpaperPath();

        _blurredWallpaper = path is null ? null : WallpaperService.CreateBlurred(path, w.BlurRadius);
        WallpaperImage.Source = _blurredWallpaper;
        _wallpaper.Watch(path);

        // Kart konumları yerleşim bittikten sonra kesinleşir
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
                var bounds = card.TransformToAncestor(DesignRoot)
                    .TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                rects.Add(bounds);
            }

            BackdropImage.Source = WallpaperService.ComposeBackdrop(_blurredWallpaper, rects,
                Math.Clamp(_settings.Wallpaper.DimOpacity, 0, 1));
        }
        catch (Exception ex)
        {
            Log.Error("Arka plan oluşturulamadı", ex);
        }
    }

    private void DisposeWidgets()
    {
        foreach (var vm in _widgets) vm.Dispose();
        _widgets.Clear();
    }

    // ---------- Olaylar ----------

    /// <summary>Monitör takılıp çıkarıldı / çözünürlük değişti: tam ekran ve büyütülmüş modda yeniden yerleştir.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (_mode != DisplayMode.Windowed) ApplyMode();
        });

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop)
            Dispatcher.InvokeAsync(ApplyWallpaper);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await LoadAllAsync(reloadSettings: true);
        }
        else if (e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleFullScreen();
        }
        else if (e.Key == Key.Q && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            Close();
        }
    }

    private async void Reload_Click(object sender, RoutedEventArgs e) => await LoadAllAsync(reloadSettings: true);

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenShell(SettingsLoader.BaseDirectory);

    private void OpenLog_Click(object sender, RoutedEventArgs e) => OpenShell(Log.FilePath);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        var enabled = Autostart.Toggle();
        if (sender is MenuItem item) item.IsChecked = enabled;
    }

    /// <summary>Menü her açıldığında gerçek durumu göster (Görev Yöneticisi'nden de kapatılmış olabilir).</summary>
    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => AutostartItem.IsChecked = Autostart.IsEnabled();

    private static void OpenShell(string target)
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

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _wallpaper.Dispose();
        DisposeWidgets();
    }
}
