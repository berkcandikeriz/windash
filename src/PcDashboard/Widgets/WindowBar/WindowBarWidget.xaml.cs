using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PcDashboard.Core;

namespace PcDashboard.Widgets.WindowBar;

/// <summary>Pencere kartı. Düğmeler ana pencerenin <see cref="IWindowHost"/> işlevlerini çağırır.</summary>
public partial class WindowBarWidget : UserControl
{
    private IWindowHost? _host;

    public WindowBarWidget()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _host = Window.GetWindow(this) as IWindowHost;
        if (_host is null) return;
        _host.DisplayModeChanged += OnModeChanged;
        Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_host is not null) _host.DisplayModeChanged -= OnModeChanged;
    }

    private void OnModeChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (_host is null) return;

        ShellTools.Visibility = _host.IsFullScreen ? Visibility.Visible : Visibility.Collapsed;

        MaximizeIcon.Data = (Geometry)FindResource(_host.IsMaximized ? "IconRestore" : "IconMaximize");
        MaximizeButton.ToolTip = _host.IsMaximized
            ? "Pencere boyutuna dön"
            : "Büyüt · görev çubuğu görünür kalır";

        FullScreenIcon.Data = (Geometry)FindResource(_host.IsFullScreen ? "IconExitFullscreen" : "IconFullscreen");
        FullScreenButton.ToolTip = _host.IsFullScreen
            ? "Tam ekrandan çık (F11)"
            : "Tam ekran · görev çubuğunu da kaplar (F11)";

        Cursor = _host.IsWindowed ? Cursors.SizeAll : null;
        ToolTip = _host.IsWindowed ? "Taşımak için sürükle · büyütmek için çift tıkla" : null;
    }

    /// <summary>Kartın boş yerine çift tık: büyüt / geri al. Pencere modunda sürükleyerek taşı.</summary>
    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_host is null) return;
        if (e.ClickCount == 2)
        {
            _host.ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) _host.BeginDrag();
    }

    private void ShowDesktop_Click(object sender, RoutedEventArgs e) => _host?.ShowDesktop();
    private void Start_Click(object sender, RoutedEventArgs e) => _host?.OpenStartMenu();
    private void Search_Click(object sender, RoutedEventArgs e) => _host?.OpenSearch();
    private void Minimize_Click(object sender, RoutedEventArgs e) => _host?.MinimizeWindow();
    private void Maximize_Click(object sender, RoutedEventArgs e) => _host?.ToggleMaximize();
    private void FullScreen_Click(object sender, RoutedEventArgs e) => _host?.ToggleFullScreen();
    private void Close_Click(object sender, RoutedEventArgs e) => _host?.CloseWindow();
}
