namespace PcDashboard.Core;

/// <summary>Pencere kartının (küçült / büyüt / tam ekran / kapat, Masaüstü / Başlat / Ara) ana pencereyle konuştuğu arayüz.</summary>
public interface IWindowHost
{
    bool IsFullScreen { get; }
    bool IsMaximized { get; }
    bool IsWindowed { get; }

    /// <summary>Pencere modu değişince.</summary>
    event EventHandler? DisplayModeChanged;

    void MinimizeWindow();
    void ToggleMaximize();
    void ToggleFullScreen();
    void CloseWindow();

    /// <summary>Pencere modunda kartı tutup sürükleyerek taşımak için.</summary>
    void BeginDrag();

    void ShowDesktop();
    void OpenStartMenu();
    void OpenSearch();
}
