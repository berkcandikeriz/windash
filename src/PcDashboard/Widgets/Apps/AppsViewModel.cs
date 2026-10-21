using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Apps;

/// <summary>Başlatıcıdaki bir kutucuk.</summary>
public sealed partial class AppTile : ObservableObject
{
    public AppTile(AppEntry entry) => Entry = entry;

    public AppEntry Entry { get; }
    public string Name => Entry.Name;

    [ObservableProperty] private string? _target;
    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _isMissing;
    [ObservableProperty] private string _tooltip = "";
}

/// <summary>
/// Sık kullanılan uygulamalar. Liste kartta düzenlenebilir (ekle / kaldır / sırala / konumunu seç) ve
/// state.json'a kaydedilir; hiç düzenlenmediyse appsettings.json → Apps.Defaults kullanılır.
/// </summary>
public sealed partial class AppsViewModel : WidgetViewModel
{
    private readonly AppsSettings _settings;
    private List<InstalledApp> _installed = new();

    public AppsViewModel(AppsSettings settings)
    {
        _settings = settings;
        Columns = Math.Clamp(settings.Columns, 2, 12);
    }

    public int Columns { get; }

    public ObservableCollection<AppTile> Tiles { get; } = new();

    public IReadOnlyList<InstalledApp> Installed => _installed;

    public override Task StartAsync()
    {
        foreach (var entry in LoadEntries()) Tiles.Add(new AppTile(entry));
        // Yeni kurulan / kaldırılan uygulamalar için arada bir yeniden çöz
        EveryAsync(TimeSpan.FromMinutes(10), () => ResolveAllAsync(refresh: true), runNow: false);
        return ResolveAllAsync(refresh: false);
    }

    private List<AppEntry> LoadEntries()
    {
        var saved = AppState.Current.Apps;
        var source = saved ?? _settings.Defaults;
        // Kopya: ayarlardaki listeyi değiştirmeyelim
        return source.Select(Clone).ToList();
    }

    private static AppEntry Clone(AppEntry e) => new()
    {
        Name = e.Name,
        Path = e.Path,
        Find = e.Find.ToList(),
        Arguments = e.Arguments,
        Optional = e.Optional,
    };

    private async Task ResolveAllAsync(bool refresh)
    {
        _installed = await AppCatalog.GetInstalledAsync(refresh);
        foreach (var tile in Tiles) Resolve(tile);

        // Varsayılan listedeki yaygın uygulamalardan bu bilgisayarda olmayanları gösterme
        foreach (var missing in Tiles.Where(t => t.Entry.Optional && t.Target is null).ToList()) Tiles.Remove(missing);

        // Hiç düzenlenmemiş varsayılan listede iki satırdan fazlasını gösterme
        if (AppState.Current.Apps is null)
            while (Tiles.Count > Columns * 2) Tiles.RemoveAt(Tiles.Count - 1);
    }

    private void Resolve(AppTile tile)
    {
        var target = AppCatalog.Resolve(tile.Entry, _installed);
        if (target != tile.Target || tile.Icon is null)
        {
            tile.Target = target;
            tile.Icon = target is null ? null : AppCatalog.GetIcon(target);
        }

        tile.IsMissing = target is null;
        tile.Tooltip = target is null
            ? $"{tile.Name} bulunamadı · Sağ tık > Konumunu seç"
            : $"{tile.Name}\nSağ tık: sırala / kaldır";
    }

    // ---------- Başlat ----------

    [RelayCommand]
    private void Launch(AppTile? tile)
    {
        if (tile?.Target is null) return;
        AppCatalog.Launch(tile.Target, tile.Entry.Arguments);
    }

    // ---------- Düzenleme (görünüm çağırır) ----------

    public void Add(InstalledApp app)
    {
        if (Tiles.Any(t => string.Equals(t.Target, app.Target, StringComparison.OrdinalIgnoreCase))) return;
        var tile = new AppTile(new AppEntry { Name = app.Name, Path = app.Target });
        Tiles.Add(tile);
        Resolve(tile);
        Save();
    }

    public void AddFile(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var tile = new AppTile(new AppEntry { Name = name, Path = path });
        Tiles.Add(tile);
        Resolve(tile);
        Save();
    }

    public void Relocate(AppTile tile, string path)
    {
        tile.Entry.Path = path;
        tile.Icon = null;
        Resolve(tile);
        Save();
    }

    public void Remove(AppTile tile)
    {
        Tiles.Remove(tile);
        Save();
    }

    public void Move(AppTile tile, int delta)
    {
        var index = Tiles.IndexOf(tile);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Tiles.Count) return;
        Tiles.Move(index, target);
        Save();
    }

    public void ResetToDefaults()
    {
        AppState.Current.Apps = null;
        AppState.Current.Save();
        Tiles.Clear();
        foreach (var entry in LoadEntries()) Tiles.Add(new AppTile(entry));
        _ = ResolveAllAsync(refresh: false);
    }

    public async Task<List<InstalledApp>> SearchAsync(string text)
    {
        if (_installed.Count == 0) _installed = await AppCatalog.GetInstalledAsync();
        var q = text.Trim();
        return _installed
            .Where(a => q.Length == 0 || a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Where(a => !Tiles.Any(t => string.Equals(t.Target, a.Target, StringComparison.OrdinalIgnoreCase)))
            .Take(60)
            .ToList();
    }

    private void Save()
    {
        AppState.Current.Apps = Tiles.Select(t => Clone(t.Entry)).ToList();
        AppState.Current.Save();
    }
}
