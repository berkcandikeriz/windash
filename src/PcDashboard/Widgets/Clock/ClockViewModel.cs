using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Clock;

public sealed partial class ClockViewModel : WidgetViewModel
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly AppSettings _settings;
    private int _lastSecond = -1;

    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _zone = "";
    [ObservableProperty] private string _time = "";
    [ObservableProperty] private string _seconds = "";
    [ObservableProperty] private string _date = "";
    [ObservableProperty] private string _week = "";

    public ClockViewModel(AppSettings settings)
    {
        _settings = settings;
        ShowSeconds = settings.Clock.ShowSeconds;
    }

    public bool ShowSeconds { get; }

    public override Task StartAsync()
    {
        // Saniye geçişini kaçırmamak için 250 ms'de bir bakıp yalnızca saniye değişince güncelliyoruz
        Every(TimeSpan.FromMilliseconds(250), Update);
        return Task.CompletedTask;
    }

    private void Update()
    {
        var now = DateTime.Now;
        if (now.Second == _lastSecond) return;
        _lastSecond = now.Second;

        Time = now.ToString(_settings.Clock.Use24Hour ? "HH:mm" : "h:mm", Tr);
        Seconds = ":" + now.ToString("ss", Tr);
        Date = now.ToString("d MMMM yyyy, dddd", Tr);
        Week = $"{ISOWeek.GetWeekOfYear(now)}. hafta";

        var first = UserInfo.FirstName(_settings.UserName);
        var name = string.IsNullOrWhiteSpace(first) ? "" : $", {first}";
        Greeting = GreetingFor(now.Hour) + name;

        var offset = TimeZoneInfo.Local.GetUtcOffset(now);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        var offsetText = abs.Minutes == 0 ? $"{abs.Hours}" : $"{abs.Hours}:{abs.Minutes:00}";
        var place = !string.IsNullOrWhiteSpace(_settings.Clock.LocationLabel)
            ? _settings.Clock.LocationLabel
            : LocationService.Current?.City ?? "";
        Zone = string.IsNullOrWhiteSpace(place) ? $"UTC{sign}{offsetText}" : $"{place} · UTC{sign}{offsetText}";
    }

    private static string GreetingFor(int hour) => hour switch
    {
        >= 5 and < 12 => "Günaydın",
        >= 12 and < 18 => "İyi günler",
        >= 18 and < 23 => "İyi akşamlar",
        _ => "İyi geceler",
    };
}
