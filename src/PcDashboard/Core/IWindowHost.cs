namespace PcDashboard.Core;

/// <summary>Pencere kartının (küçült / büyüt / tam ekran / kapat, Masaüstü / Başlat / Ara) ana pencereyle konuştuğu arayüz.</summary>
public interface IWindowHost
{
    bool IsFullScreen { get; }
    bool IsMaximized { get; }
    bool IsWindowed { get; }

    /// <summary>"Masaüstüne göm" açık mı.</summary>
    bool IsDesktopMode { get; }

    /// <summary>Pencere modu veya masaüstüne gömme değişince.</summary>
    event EventHandler? DisplayModeChanged;

    void MinimizeWindow();
    void ToggleMaximize();
    void ToggleFullScreen();
    void CloseWindow();

    /// <summary>Düzenle: kartları taşı ve boyutlandır.</summary>
    void EditLayout();

    /// <summary>Masaüstüne göm: pencere gizlenir, kartlar masaüstünün sağına yerleşir.</summary>
    void ToggleDesktopMode();

    /// <summary>Pencere modunda kartı tutup sürükleyerek taşımak için.</summary>
    void BeginDrag();

    void ShowDesktop();
    void OpenStartMenu();
    void OpenSearch();
}
