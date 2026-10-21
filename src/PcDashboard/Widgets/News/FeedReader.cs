using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PcDashboard.Core;
using PcDashboard.Services;

namespace PcDashboard.Widgets.News;

public sealed record FeedEntry(string Title, string Link, DateTimeOffset? Published, string Source, string Category);

/// <summary>RSS 2.0 ve Atom beslemelerini okur. Standart dışı tarihlere karşı toleranslıdır.</summary>
public static partial class FeedReader
{
    public static async Task<List<FeedEntry>> ReadAsync(FeedConfig feed)
    {
        await using var stream = await Http.Client.GetStreamAsync(feed.Url);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, Async = true };
        using var reader = XmlReader.Create(stream, settings);
        var doc = await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None);

        var items = doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry");
        var list = new List<FeedEntry>();

        foreach (var item in items)
        {
            var title = Clean(Child(item, "title"));
            if (string.IsNullOrWhiteSpace(title)) continue;

            var link = Child(item, "link");
            if (string.IsNullOrWhiteSpace(link))
            {
                link = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link")?.Attribute("href")?.Value ?? "";
            }

            var date = ParseDate(Child(item, "pubDate") ?? Child(item, "published") ?? Child(item, "updated") ?? Child(item, "date"));
            list.Add(new FeedEntry(title, link.Trim(), date, feed.Name, feed.Category));
        }

        return list;
    }

    private static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var noTags = TagRegex().Replace(text, "");
        return WebUtility.HtmlDecode(WhitespaceRegex().Replace(noTags, " ")).Trim();
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();

        // "+0300" → "+03:00", "GMT"/"UT" → "+00:00"
        v = OffsetRegex().Replace(v, "$1$2:$3");
        v = Regex.Replace(v, @"\b(GMT|UTC|UT)$", "+00:00");

        if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result))
            return result;

        // Bazı Türkçe beslemeler gün/ay adlarını Türkçe yazıyor
        if (DateTimeOffset.TryParse(v, new CultureInfo("tr-TR"), DateTimeStyles.AllowWhiteSpaces, out result))
            return result;

        Log.Warn($"Haber tarihi çözülemedi: {value}");
        return null;
    }

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(\s)([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetRegex();
}
