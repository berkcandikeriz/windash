using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Agenda;

/// <summary>Takvim ızgarasındaki bir hücre: gün başlığı (Pzt…), boş hücre veya ayın bir günü.</summary>
public sealed record CalendarCell(string Text, bool IsHeader, bool IsToday, bool HasEvent);

public sealed record AgendaItem(string Title, string TimeText, Brush Color);

public sealed partial class AgendaViewModel : WidgetViewModel
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly CalendarSettings _settings;
    private List<CalendarEvent> _events = new();
    private DateTime _renderedDate;

    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _footer = "";
    [ObservableProperty] private bool _hasNoEvents = true;
    [ObservableProperty] private string _emptyText = "";

    private static readonly string[] Weekdays = { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };

    /// <summary>Taslaktaki gibi: 7 gün başlığı, ayın ilk gününe kadar boş hücreler, sonra ayın günleri.</summary>
    public ObservableCollection<CalendarCell> Cells { get; } = new();
    public ObservableCollection<AgendaItem> Today { get; } = new();

    public AgendaViewModel(CalendarSettings settings)
    {
        _settings = settings;
        EmptyText = settings.IcsUrls.Count == 0
            ? "Takvim bağlı değil.\nappsettings.json → Calendar.IcsUrls"
            : "Bugün etkinlik yok";
    }

    public override Task StartAsync()
    {
        Render();
        // Gece yarısı geçince takvimi yeniden çiz
        Every(TimeSpan.FromMinutes(1), () =>
        {
            if (DateTime.Today != _renderedDate) Render();
        }, runNow: false);

        if (_settings.IcsUrls.Count > 0)
            EveryAsync(TimeSpan.FromMinutes(Math.Max(5, _settings.RefreshMinutes)), LoadEventsAsync);

        return Task.CompletedTask;
    }

    private async Task LoadEventsAsync()
    {
        var today = DateTime.Today;
        var from = new DateTime(today.Year, today.Month, 1).AddDays(-7);
        var to = from.AddDays(50);

        var all = new List<CalendarEvent>();
        foreach (var url in _settings.IcsUrls)
        {
            try
            {
                var fixedUrl = url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
                    ? "https://" + url["webcal://".Length..]
                    : url;
                var text = await Http.Client.GetStringAsync(fixedUrl);
                all.AddRange(IcsParser.Parse(text, from, to));
            }
            catch (Exception ex)
            {
                Log.Error("Takvim okunamadı", ex);
            }
        }

        _events = all;
        Render();
    }

    private void Render()
    {
        var today = DateTime.Today;
        _renderedDate = today;
        MonthTitle = today.ToString("MMMM yyyy", Tr);

        var first = new DateTime(today.Year, today.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // Pazartesi = 0
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);

        var eventDays = new HashSet<DateTime>();
        foreach (var e in _events)
        {
            for (var d = e.Start.Date; d < e.End || d == e.Start.Date; d = d.AddDays(1))
            {
                eventDays.Add(d);
                if (d > e.Start.Date.AddDays(60)) break;
            }
        }

        Cells.Clear();
        foreach (var w in Weekdays) Cells.Add(new CalendarCell(w, true, false, false));
        for (var i = 0; i < offset; i++) Cells.Add(new CalendarCell("", false, false, false));
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = first.AddDays(day - 1);
            Cells.Add(new CalendarCell(day.ToString(Tr), false, date == today, eventDays.Contains(date)));
        }

        // Bugünün etkinlikleri
        Today.Clear();
        var accent = ResourceBrush("AccentBrush");
        var warn = ResourceBrush("WarnBrush");
        var todays = _events
            .Where(e => e.Start.Date <= today && (e.End > today || e.Start.Date == today))
            .OrderBy(e => !e.AllDay)
            .ThenBy(e => e.Start)
            .Take(3)
            .ToList();

        for (var i = 0; i < todays.Count; i++)
        {
            var e = todays[i];
            var time = e.AllDay ? "Tüm gün" : $"{e.Start:HH:mm} – {e.End:HH:mm}";
            Today.Add(new AgendaItem(e.Title, time, i % 2 == 0 ? accent : warn));
        }

        HasNoEvents = Today.Count == 0;

        var tomorrow = today.AddDays(1);
        var tomorrowCount = _events.Count(e => e.Start.Date == tomorrow);
        Footer = _settings.IcsUrls.Count == 0 ? "" : tomorrowCount == 0 ? "Yarın etkinlik yok" : $"Yarın: {tomorrowCount} etkinlik";
    }
}
