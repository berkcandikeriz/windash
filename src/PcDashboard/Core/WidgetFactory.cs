using System.Windows;
using PcDashboard.Widgets.Agenda;
using PcDashboard.Widgets.Apps;
using PcDashboard.Widgets.Clock;
using PcDashboard.Widgets.ControlCenter;
using PcDashboard.Widgets.Devices;
using PcDashboard.Widgets.Network;
using PcDashboard.Widgets.News;
using PcDashboard.Widgets.NowPlaying;
using PcDashboard.Widgets.SystemInfo;
using PcDashboard.Widgets.Weather;
using PcDashboard.Widgets.WindowBar;

namespace PcDashboard.Core;

public sealed record WidgetInstance(FrameworkElement View, WidgetViewModel ViewModel, Thickness Padding);

/// <summary>
/// layout.json'daki "Type" değerini widget'a çevirir. Yeni widget eklemek için buraya bir satır ekle.
/// İç boşluklar taslaktaki CSS padding değerleridir (+1px kenarlık).
/// </summary>
public static class WidgetFactory
{
    private static Thickness Pad(double vertical, double horizontal) =>
        new(horizontal + 1, vertical + 1, horizontal + 1, vertical + 1);

    public static WidgetInstance? Create(WidgetPlacement p, AppSettings s)
    {
        var compact = string.Equals(p.Variant, "compact", StringComparison.OrdinalIgnoreCase);

        return p.Type.Trim().ToLowerInvariant() switch
        {
            "window" => new(new WindowBarWidget(), new WindowBarViewModel(), Pad(12, 16)),
            "clock" => new(new ClockWidget(), new ClockViewModel(s), Pad(28, 32)),
            "weather" => new(new WeatherWidget(), new WeatherViewModel(s.Weather, s.Clock), Pad(28, 32)),
            "apps" => new(new AppsWidget(), new AppsViewModel(s.Apps), Pad(24, 32)),
            "calendar" => new(new AgendaWidget(), new AgendaViewModel(s.Calendar), Pad(24, 32)),
            "media" => new(new NowPlayingWidget(), new NowPlayingViewModel(s.Media), Pad(28, 32)),
            "news" => new(new NewsWidget(), new NewsViewModel(s.News), Pad(28, 32)),
            "devices" => new(new DevicesWidget(), new DevicesViewModel(s.Devices), Pad(28, 28)),
            "controls" => new(new ControlCenterWidget(), new ControlCenterViewModel(s), Pad(22, 24)),
            "network" => new(new NetworkWidget(), new NetworkViewModel(s.Network), Pad(20, 28)),
            "system" => new(new SystemInfoWidget(), new SystemInfoViewModel(s.SystemMonitor, compact),
                compact ? Pad(20, 28) : Pad(24, 28)),
            _ => null,
        };
    }
}
