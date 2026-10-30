using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PcDashboard.Controls;
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
    private LayoutConfig _layout = new();
    private LayoutEditor? _editor;
    private System.Windows.Controls.Primitives.Popup? _editBar;

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
        // Dışarıdan kapatıldıysa (WM_CLOSE: kurulum programı, oturum kapanışı) uygulama da kapansın
        Closing += (_, _) =>
        {
            if (!App.Current.IsExiting) App.Current.BeginExit();
        };
        PreviewKeyDown += OnPreviewKeyDown;

        _wallpaper.Changed += (_, _) => ApplyWallpaper();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>F5 / menü > Yenile. Pencere hiç açılmadıysa bir şey yapmaz (açıldığında zaten yüklenir).</summary>
    public Task ReloadAsync() => IsLoaded ? LoadAllAsync(reloadSettings: true) : Task.CompletedTask;

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

            EndEdit(keep: false);
            DisposeWidgets();
            _layout = SettingsLoader.LoadLayout();
            BuildGrid(_layout);
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
                Tag = placement,
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

    // ---------- Düzenle ----------

    public bool IsEditing => _editor is not null;

    /// <summary>Kartları taşıma / boyutlandırma modu (12 x 12 ızgara).</summary>
    public void BeginEdit()
    {
        if (IsEditing || !IsLoaded) return;
        ShowDashboard();
        _editor = new LayoutEditor(WidgetGrid, _cards, ComposeBackdrop);
        _editor.Begin();
        _editBar = EditToolbar.Show(DesignRoot, "Kartı sürükle: taşı · sağ alt köşe: boyutlandır",
            done: () => EndEdit(keep: true),
            cancel: () => EndEdit(keep: false),
            reset: ResetLayout);
        DisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndEdit(bool keep)
    {
        if (_editor is null) return;
        _editor.End(keep);
        _editor = null;
        if (_editBar is not null) _editBar.IsOpen = false;
        _editBar = null;

        if (keep) SaveLayout();
        ComposeBackdrop();
        DisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Yerleşimi kullanıcının layout.json kopyasına yazar (%LOCALAPPDATA%\WinDash).</summary>
    private void SaveLayout()
    {
        try
        {
            _layout.Widgets = _cards.Select(c => c.Tag).OfType<WidgetPlacement>()
                .Concat(_layout.Widgets.Where(w => !_cards.Any(c => ReferenceEquals(c.Tag, w))))
                .Distinct()
                .ToList();
            var json = System.Text.Json.JsonSerializer.Serialize(_layout,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.UserFile("layout.json"), json);
            Log.Info("Yerleşim kaydedildi");
        }
        catch (Exception ex)
        {
            Log.Error("Yerleşim kaydedilemedi", ex);
        }
    }

    private async void ResetLayout()
    {
        EndEdit(keep: false);
        try
        {
            var source = AppPaths.DefaultFile("layout.json");
            if (File.Exists(source)) File.Copy(source, AppPaths.UserFile("layout.json"), overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Varsayılan yerleşim geri yüklenemedi", ex);
        }

        await LoadAllAsync(reloadSettings: false);
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => BeginEdit();

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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            App.Current.ReloadAll();
        }
        else if (e.Key == Key.F11)
        {
            e.Handled = true;
            ToggleFullScreen();
        }
        else if (e.Key == Key.Q && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            App.Current.ExitApp();
        }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => App.Current.ReloadAll();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => App.OpenShell(SettingsLoader.BaseDirectory);

    private void OpenLog_Click(object sender, RoutedEventArgs e) => App.OpenShell(Log.FilePath);

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private void DesktopMode_Click(object sender, RoutedEventArgs e) => ToggleDesktopMode();

    private void HideToTray_Click(object sender, RoutedEventArgs e) => App.Current.HideDashboard();

    private void LockWithStandby_Click(object sender, RoutedEventArgs e) => App.Current.LockWithStandby();

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        var enabled = Autostart.Toggle();
        if (sender is MenuItem item) item.IsChecked = enabled;
    }

    /// <summary>Menü her açıldığında gerçek durumu göster (Görev Yöneticisi'nden de kapatılmış olabilir).</summary>
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        AutostartItem.IsChecked = Autostart.IsEnabled();
        DesktopModeItem.IsChecked = App.Current.IsDesktopMode;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _wallpaper.Dispose();
        DisposeWidgets();
    }
}
