using System.Globalization;
using System.Text.Json;
using PcDashboard.Core;
using Windows.Devices.Geolocation;

namespace PcDashboard.Services;

public sealed record GeoLocation(double Latitude, double Longitude, string City);

/// <summary>
/// Hava durumu ve saat kartı için konum. Sıra:
///  1. appsettings.json → Weather.Latitude / Longitude (elle yazıldıysa)
///  2. Windows konum hizmeti (Ayarlar > Gizlilik > Konum'da masaüstü uygulamalarına izin verildiyse)
///  3. IP adresinden şehir düzeyinde tahmin
/// Bulunan konum state.json'a kaydedilir ve günde bir yenilenir.
/// </summary>
public static class LocationService
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static GeoLocation? _current;

    /// <summary>Bilinen son konum (henüz bulunmadıysa null).</summary>
    public static GeoLocation? Current => _current ??= FromState();

    public static async Task<GeoLocation?> GetAsync(WeatherSettings weather, ClockSettings clock)
    {
        if (weather.Latitude is { } lat && weather.Longitude is { } lon)
            return _current = new GeoLocation(lat, lon, clock.LocationLabel);

        await Gate.WaitAsync();
        try
        {
            var saved = AppState.Current.Location;
            if (saved is not null && DateTime.UtcNow - saved.UpdatedUtc < MaxAge)
                return _current = new GeoLocation(saved.Latitude, saved.Longitude, saved.City);

            var found = await FromWindowsAsync() ?? await FromIpAsync();
            if (found is null) return _current = FromState(); // eski de olsa bilinen konum

            AppState.Current.Location = new SavedLocation
            {
                Latitude = found.Latitude,
                Longitude = found.Longitude,
                City = found.City,
                UpdatedUtc = DateTime.UtcNow,
            };
            AppState.Current.Save();
            Log.Info($"Konum: {found.City} ({found.Latitude:0.00}, {found.Longitude:0.00})");
            return _current = found;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static GeoLocation? FromState() =>
        AppState.Current.Location is { } s ? new GeoLocation(s.Latitude, s.Longitude, s.City) : null;

    private static async Task<GeoLocation?> FromWindowsAsync()
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed) return null;

            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            var p = position.Coordinate.Point.Position;
            var city = await ReverseGeocodeAsync(p.Latitude, p.Longitude) ?? "";
            return new GeoLocation(p.Latitude, p.Longitude, city);
        }
        catch (Exception ex)
        {
            Log.Warn($"Windows konumu alınamadı: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> ReverseGeocodeAsync(double lat, double lon)
    {
        try
        {
            var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var url = "https://api.bigdatacloud.net/data/reverse-geocode-client" +
                      $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}" +
                      $"&longitude={lon.ToString(CultureInfo.InvariantCulture)}&localityLanguage={lang}";
            using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync(url));
            foreach (var key in new[] { "city", "locality", "principalSubdivision" })
            {
                if (doc.RootElement.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } s) return s;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Şehir adı bulunamadı: {ex.Message}");
        }

        return null;
    }

    private static async Task<GeoLocation?> FromIpAsync()
    {
        try
        {
            var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            using var doc = JsonDocument.Parse(await Http.Client.GetStringAsync($"https://ipwho.is/?lang={lang}"));
            var root = doc.RootElement;
            if (!root.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return null;
            return new GeoLocation(
                root.GetProperty("latitude").GetDouble(),
                root.GetProperty("longitude").GetDouble(),
                root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "");
        }
        catch (Exception ex)
        {
            Log.Warn($"IP'den konum bulunamadı: {ex.Message}");
            return null;
        }
    }
}
