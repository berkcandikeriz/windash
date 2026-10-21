using System.Globalization;
using System.Text;

namespace PcDashboard.Widgets.Agenda;

public sealed record CalendarEvent(string Title, DateTime Start, DateTime End, bool AllDay);

/// <summary>
/// Basit iCalendar (.ics) okuyucu. Tek seferlik etkinlikleri ve günlük/haftalık tekrarları destekler.
/// Daha karmaşık tekrar kuralları için Ical.Net paketine geçilebilir.
/// </summary>
public static class IcsParser
{
    public static List<CalendarEvent> Parse(string text, DateTime rangeStart, DateTime rangeEnd)
    {
        var result = new List<CalendarEvent>();
        Dictionary<string, (string Params, string Value)>? current = null;

        foreach (var line in Unfold(text))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) Expand(current, rangeStart, rangeEnd, result);
                current = null;
                continue;
            }

            if (current is null) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var nameAndParams = line[..colon];
            var value = line[(colon + 1)..];
            var semi = nameAndParams.IndexOf(';');
            var name = semi < 0 ? nameAndParams : nameAndParams[..semi];
            var parameters = semi < 0 ? "" : nameAndParams[(semi + 1)..];
            current.TryAdd(name, (parameters, value));
        }

        return result;
    }

    private static void Expand(Dictionary<string, (string Params, string Value)> e, DateTime from, DateTime to,
        List<CalendarEvent> output)
    {
        if (!e.TryGetValue("DTSTART", out var startRaw)) return;
        if (e.TryGetValue("STATUS", out var status) && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return;

        var (start, allDay) = ParseDate(startRaw.Params, startRaw.Value);
        if (start is null) return;

        DateTime end;
        if (e.TryGetValue("DTEND", out var endRaw) && ParseDate(endRaw.Params, endRaw.Value).Date is { } parsedEnd)
            end = parsedEnd;
        else
            end = allDay ? start.Value.AddDays(1) : start.Value.AddHours(1);

        var title = e.TryGetValue("SUMMARY", out var summary) ? Unescape(summary.Value) : "(Başlıksız)";
        var duration = end - start.Value;

        if (!e.TryGetValue("RRULE", out var rrule))
        {
            if (end > from && start.Value < to) output.Add(new CalendarEvent(title, start.Value, end, allDay));
            return;
        }

        // Basit tekrar desteği: FREQ=DAILY|WEEKLY, INTERVAL, UNTIL, COUNT
        var rule = rrule.Value.Split(';')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);

        var freq = rule.GetValueOrDefault("FREQ", "").ToUpperInvariant();
        var step = freq switch
        {
            "DAILY" => TimeSpan.FromDays(1),
            "WEEKLY" => TimeSpan.FromDays(7),
            _ => TimeSpan.Zero,
        };

        if (step == TimeSpan.Zero)
        {
            // Desteklenmeyen tekrar: yalnızca ilk örneği göster
            if (end > from && start.Value < to) output.Add(new CalendarEvent(title, start.Value, end, allDay));
            return;
        }

        var interval = int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) && iv > 0 ? iv : 1;
        var count = int.TryParse(rule.GetValueOrDefault("COUNT"), out var c) ? c : int.MaxValue;
        var until = rule.TryGetValue("UNTIL", out var u) ? ParseDate("", u).Date ?? DateTime.MaxValue : DateTime.MaxValue;

        var occurrence = start.Value;
        var emitted = 0;
        while (occurrence < to && occurrence <= until && emitted < count)
        {
            if (occurrence + duration > from)
                output.Add(new CalendarEvent(title, occurrence, occurrence + duration, allDay));

            emitted++;
            occurrence += step * interval;
            if (emitted > 5000) break;
        }
    }

    private static (DateTime? Date, bool AllDay) ParseDate(string parameters, string value)
    {
        value = value.Trim();

        if (value.Length == 8 &&
            DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return (day, true);
        }

        var isUtc = value.EndsWith('Z');
        var core = value.TrimEnd('Z');
        if (!DateTime.TryParseExact(core, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return (null, false);

        if (isUtc) return (DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime(), false);

        var tzid = parameters.Split(';')
            .Select(p => p.Split('=', 2))
            .FirstOrDefault(p => p.Length == 2 && p[0].Equals("TZID", StringComparison.OrdinalIgnoreCase))?[1]
            .Trim('"');

        if (!string.IsNullOrWhiteSpace(tzid))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(tzid);
                return (TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified), zone, TimeZoneInfo.Local), false);
            }
            catch
            {
                // Bilinmeyen saat dilimi: yerel saat kabul et
            }
        }

        return (dt, false);
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                sb.Append(raw, 1, raw.Length - 1);
                continue;
            }

            if (sb.Length > 0) yield return sb.ToString();
            sb.Clear();
            sb.Append(raw);
        }

        if (sb.Length > 0) yield return sb.ToString();
    }

    private static string Unescape(string s) =>
        s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
}
