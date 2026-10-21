using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.Weather;

public sealed record DayForecast(string Day, string Max, string Min, Geometry Icon);

/// <summary>Open-Meteo (ücretsiz, anahtarsız) ile anlık durum ve 5 günlük tahmin.</summary>
public sealed partial class WeatherViewModel : WidgetViewModel
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private readonly WeatherSettings _settings;
    private readonly ClockSettings _clock;

    [ObservableProperty] private string _temperature = "—";
    [ObservableProperty] private string _description = "Yükleniyor…";
    [ObservableProperty] private string _highLow = "";
    [ObservableProperty] private string _humidityWind = "";
    [ObservableProperty] private string _precipitation = "";
    [ObservableProperty] private string _status = "Open-Meteo";
    [ObservableProperty] private Geometry _icon;

    public ObservableCollection<DayForecast> Days { get; } = new();

    public WeatherViewModel(WeatherSettings settings, ClockSettings clock)
    {
        _settings = settings;
        _clock = clock;
        _icon = ResourceGeometry("IconPartlyCloudy");
    }

    public override Task StartAsync()
    {
        EveryAsync(TimeSpan.FromMinutes(Math.Max(5, _settings.RefreshMinutes)), RefreshAsync);
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        var location = await LocationService.GetAsync(_settings, _clock);
        if (location is null)
        {
            Description = "Konum bulunamadı";
            Status = "Ayarlar > Gizlilik > Konum'u aç veya Weather.Latitude/Longitude yaz";
            return;
        }

        var lat = location.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = location.Longitude.ToString(CultureInfo.InvariantCulture);
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={lat}&longitude={lon}" +
                  "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  "&timezone=auto&forecast_days=6";

        try
        {
            await using var stream = await Http.Client.GetStreamAsync(url);
            using var doc = await JsonDocument.ParseAsync(stream);
            Apply(doc.RootElement);
            Status = $"Open-Meteo · {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            Log.Error("Hava durumu alınamadı", ex);
            Status = "Bağlantı hatası · yeniden denenecek";
        }
    }

    private void Apply(JsonElement root)
    {
        var current = root.GetProperty("current");
        var temp = current.GetProperty("temperature_2m").GetDouble();
        var feels = current.GetProperty("apparent_temperature").GetDouble();
        var humidity = current.GetProperty("relative_humidity_2m").GetDouble();
        var wind = current.GetProperty("wind_speed_10m").GetDouble();
        var code = current.GetProperty("weather_code").GetInt32();
        var isDay = current.TryGetProperty("is_day", out var d) && d.GetInt32() == 1;

        Temperature = $"{Math.Round(temp)}°";
        Description = $"{WeatherCodes.Describe(code)} · Hissedilen {Math.Round(feels)}°";
        HumidityWind = $"Nem %{Math.Round(humidity)} · Rüzgâr {Math.Round(wind)} km/s";
        Icon = ResourceGeometry(WeatherCodes.IconKey(code, isDay));

        var daily = root.GetProperty("daily");
        var times = daily.GetProperty("time");
        var maxes = daily.GetProperty("temperature_2m_max");
        var mins = daily.GetProperty("temperature_2m_min");
        var codes = daily.GetProperty("weather_code");
        var precip = daily.GetProperty("precipitation_probability_max");

        if (times.GetArrayLength() > 0)
        {
            HighLow = $"En yüksek {Math.Round(maxes[0].GetDouble())}° · En düşük {Math.Round(mins[0].GetDouble())}°";
            Precipitation = precip[0].ValueKind == JsonValueKind.Number
                ? $"Yağış ihtimali %{Math.Round(precip[0].GetDouble())}"
                : "";
        }

        Days.Clear();
        for (var i = 1; i < times.GetArrayLength() && Days.Count < 5; i++)
        {
            var date = DateTime.ParseExact(times[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Days.Add(new DayForecast(
                Tr.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek),
                $"{Math.Round(maxes[i].GetDouble())}°",
                $"{Math.Round(mins[i].GetDouble())}°",
                ResourceGeometry(WeatherCodes.IconKey(codes[i].GetInt32(), isDay: true))));
        }
    }
}

/// <summary>WMO hava durumu kodları → Türkçe açıklama ve ikon.</summary>
public static class WeatherCodes
{
    public static string Describe(int code) => code switch
    {
        0 => "Açık",
        1 => "Az bulutlu",
        2 => "Parçalı bulutlu",
        3 => "Kapalı",
        45 or 48 => "Sisli",
        51 or 53 or 55 => "Çisenti",
        56 or 57 => "Dondurucu çisenti",
        61 => "Hafif yağmur",
        63 => "Yağmurlu",
        65 => "Kuvvetli yağmur",
        66 or 67 => "Dondurucu yağmur",
        71 => "Hafif kar",
        73 => "Karlı",
        75 => "Yoğun kar",
        77 => "Kar taneleri",
        80 or 81 or 82 => "Sağanak yağış",
        85 or 86 => "Kar sağanağı",
        95 => "Gök gürültülü fırtına",
        96 or 99 => "Dolulu fırtına",
        _ => "Bilinmiyor",
    };

    public static string IconKey(int code, bool isDay) => code switch
    {
        0 or 1 => isDay ? "IconSun" : "IconMoon",
        2 => "IconPartlyCloudy",
        3 => "IconCloud",
        45 or 48 => "IconFog",
        >= 51 and <= 67 => "IconRain",
        >= 80 and <= 82 => "IconRain",
        >= 71 and <= 77 => "IconSnow",
        85 or 86 => "IconSnow",
        >= 95 => "IconStorm",
        _ => "IconCloud",
    };
}
