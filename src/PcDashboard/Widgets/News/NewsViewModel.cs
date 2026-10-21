using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcDashboard.Core;

namespace PcDashboard.Widgets.News;

public sealed partial class NewsCategory : ObservableObject
{
    public NewsCategory(string name) => Name = name;

    public string Name { get; }

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class NewsItem : ObservableObject
{
    public NewsItem(FeedEntry entry) => Entry = entry;

    public FeedEntry Entry { get; }
    public string Title => Entry.Title;

    [ObservableProperty] private string _meta = "";

    public void RefreshMeta() => Meta = $"{Entry.Source} · {Relative(Entry.Published)}";

    private static string Relative(DateTimeOffset? published)
    {
        if (published is null) return "";
        var diff = DateTimeOffset.Now - published.Value;
        if (diff < TimeSpan.FromMinutes(1)) return "az önce";
        if (diff < TimeSpan.FromHours(1)) return $"{(int)diff.TotalMinutes} dk önce";
        if (diff < TimeSpan.FromDays(1)) return $"{(int)diff.TotalHours} sa önce";
        return $"{(int)diff.TotalDays} gün önce";
    }
}

public sealed partial class NewsViewModel : WidgetViewModel
{
    private readonly NewsSettings _settings;
    private List<FeedEntry> _all = new();

    [ObservableProperty] private string _status = "";

    public ObservableCollection<NewsCategory> Categories { get; } = new();
    public ObservableCollection<NewsItem> Items { get; } = new();

    public NewsViewModel(NewsSettings settings)
    {
        _settings = settings;
        foreach (var name in settings.Feeds.Select(f => f.Category).Distinct(StringComparer.OrdinalIgnoreCase))
            Categories.Add(new NewsCategory(name));

        if (Categories.Count > 0) Categories[0].IsSelected = true;
        Status = settings.Feeds.Count == 0 ? "appsettings.json → News.Feeds boş" : "Yükleniyor…";
    }

    public override Task StartAsync()
    {
        if (_settings.Feeds.Count == 0) return Task.CompletedTask;

        EveryAsync(TimeSpan.FromMinutes(Math.Max(2, _settings.RefreshMinutes)), RefreshAsync);
        Every(TimeSpan.FromMinutes(1), () =>
        {
            foreach (var item in Items) item.RefreshMeta();
        }, runNow: false);

        if (_settings.RotateSeconds > 0)
            Every(TimeSpan.FromSeconds(_settings.RotateSeconds), RotateCategory, runNow: false);

        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        var tasks = _settings.Feeds.Select(async feed =>
        {
            try
            {
                return await FeedReader.ReadAsync(feed);
            }
            catch (Exception ex)
            {
                Log.Error($"Haber kaynağı okunamadı: {feed.Name}", ex);
                return new List<FeedEntry>();
            }
        });

        var results = await Task.WhenAll(tasks);
        var merged = results.SelectMany(r => r).ToList();

        if (merged.Count > 0)
        {
            _all = merged;
            Status = "";
        }
        else if (_all.Count == 0)
        {
            Status = "Haberler alınamadı · yeniden denenecek";
        }

        ShowSelected();
    }

    private void ShowSelected()
    {
        var category = Categories.FirstOrDefault(c => c.IsSelected)?.Name;
        var entries = _all
            .Where(e => category is null || e.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Title)
            .Select(g => g.First())
            .OrderByDescending(e => e.Published ?? DateTimeOffset.MinValue)
            .Take(Math.Max(1, _settings.MaxItems));

        Items.Clear();
        foreach (var entry in entries)
        {
            var item = new NewsItem(entry);
            item.RefreshMeta();
            Items.Add(item);
        }
    }

    private void RotateCategory()
    {
        if (Categories.Count < 2) return;
        var index = Categories.IndexOf(Categories.First(c => c.IsSelected));
        SelectCategory(Categories[(index + 1) % Categories.Count]);
    }

    [RelayCommand]
    private void SelectCategory(NewsCategory? category)
    {
        if (category is null) return;
        foreach (var c in Categories) c.IsSelected = ReferenceEquals(c, category);
        ShowSelected();
    }

    [RelayCommand]
    private void Open(NewsItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Entry.Link)) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.Entry.Link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Haber bağlantısı açılamadı", ex);
        }
    }
}
