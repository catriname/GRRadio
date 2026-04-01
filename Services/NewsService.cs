using GRRadio.Models;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace GRRadio.Services;

public class NewsService(HttpClient http)
{
    private const string DxFeedUrl = "https://dxnews.com/rss.xml";

    private (List<RedditPost> Posts, DateTime FetchedAt) _redditCache;
    private (List<DxNewsItem> Items, DateTime FetchedAt) _dxCache;

    // Matches "January 15-28, 2026" / "Feb 5 – Mar 10, 2026" / "March 2026" etc.
    private static readonly Regex DateRangeRegex = new(
        @"\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?" +
        @"|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)" +
        @"\s+\d{1,2}(?:\s*[–\-]\s*(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?" +
        @"|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?" +
        @"|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?\d{1,2})?[,\s]+\d{4}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<List<RedditPost>> GetRedditPostsAsync(List<string> subreddits)
    {
        if (_redditCache.Posts != null && _redditCache.Posts.Count > 0 &&
            (DateTime.UtcNow - _redditCache.FetchedAt).TotalMinutes < 30)
            return _redditCache.Posts;

        var all = new List<RedditPost>();
        foreach (var sub in subreddits.Distinct())
        {
            try
            {
                var json = await http.GetStringAsync(
                    $"https://www.reddit.com/r/{sub}/hot.json?limit=15&raw_json=1");
                all.AddRange(ParseRedditJson(json));
            }
            catch { /* skip failed sub */ }
        }

        var sorted = all
            .Where(p => !IsAnnouncement(p))
            .OrderByDescending(p => p.Score)
            .ToList();

        _redditCache = (sorted, DateTime.UtcNow);
        return sorted;
    }

    public async Task<List<DxNewsItem>> GetDxNewsAsync()
    {
        if (_dxCache.Items != null && _dxCache.Items.Count > 0 &&
            (DateTime.UtcNow - _dxCache.FetchedAt).TotalHours < 1)
            return _dxCache.Items;

        try
        {
            var bytes = await http.GetByteArrayAsync(DxFeedUrl);
            var xml = System.Text.Encoding.UTF8.GetString(bytes);
            var items = TryParseRssXml(xml) ?? ParseRssRegex(xml);
            _dxCache = (items, DateTime.UtcNow);
            return items;
        }
        catch
        {
            return [];
        }
    }

    private static bool IsAnnouncement(RedditPost post) =>
        (post.Flair?.Contains("announcement", StringComparison.OrdinalIgnoreCase) ?? false) ||
        post.Title.StartsWith("[announcement]", StringComparison.OrdinalIgnoreCase) ||
        post.Title.StartsWith("announcement:", StringComparison.OrdinalIgnoreCase);

    private static List<RedditPost> ParseRedditJson(string json)
    {
        var posts = new List<RedditPost>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var children = doc.RootElement
                .GetProperty("data")
                .GetProperty("children");

            foreach (var child in children.EnumerateArray())
            {
                var d = child.GetProperty("data");
                posts.Add(new RedditPost
                {
                    Title        = d.GetProperty("title").GetString() ?? "",
                    Subreddit    = d.GetProperty("subreddit").GetString() ?? "",
                    Permalink    = "https://www.reddit.com" + d.GetProperty("permalink").GetString(),
                    Score        = d.GetProperty("score").GetInt32(),
                    CommentCount = d.GetProperty("num_comments").GetInt32(),
                    Author       = d.GetProperty("author").GetString() ?? "",
                    CreatedUtc   = DateTimeOffset
                        .FromUnixTimeSeconds((long)d.GetProperty("created_utc").GetDouble())
                        .UtcDateTime,
                    Flair = d.TryGetProperty("link_flair_text", out var flair) &&
                            flair.ValueKind != JsonValueKind.Null
                                ? flair.GetString() : null
                });
            }
        }
        catch { }
        return posts;
    }

    // Primary: lenient XML parsing
    private static List<DxNewsItem>? TryParseRssXml(string xml)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing  = DtdProcessing.Ignore,
                XmlResolver    = null,
                CheckCharacters = false
            };
            using var sr = new StringReader(xml);
            using var reader = XmlReader.Create(sr, settings);
            var doc = XDocument.Load(reader);
            var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

            var items = doc.Descendants("item")
                .Take(20)
                .Select(item => BuildDxItem(
                    title:   item.Element("title")?.Value,
                    url:     item.Element("link")?.Value
                             ?? item.Element("guid")?.Value,
                    desc:    item.Element("description")?.Value,
                    pubDate: item.Element("pubDate")?.Value))
                .Where(i => !string.IsNullOrEmpty(i.Title))
                .ToList();

            return items.Count > 0 ? items : null;
        }
        catch
        {
            return null;
        }
    }

    // Fallback: regex extraction for feeds that aren't strict XML
    private static List<DxNewsItem> ParseRssRegex(string xml)
    {
        var items = new List<DxNewsItem>();
        var itemBlocks = Regex.Matches(xml, @"<item[^>]*>(.*?)</item>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match block in itemBlocks.Take(20))
        {
            var content = block.Groups[1].Value;
            items.Add(BuildDxItem(
                title:   ExtractTag(content, "title"),
                url:     ExtractTag(content, "link") ?? ExtractTag(content, "guid"),
                desc:    ExtractTag(content, "description"),
                pubDate: ExtractTag(content, "pubDate")));
        }
        return items.Where(i => !string.IsNullOrEmpty(i.Title)).ToList();
    }

    private static DxNewsItem BuildDxItem(string? title, string? url, string? desc, string? pubDate)
    {
        var cleanDesc = StripHtml(desc ?? "");
        var combined = (title ?? "") + " " + cleanDesc;
        return new DxNewsItem
        {
            Title         = StripHtml(title ?? "").Trim(),
            Url           = url?.Trim() ?? "",
            Description   = cleanDesc,
            PublishedDate = DateTime.TryParse(pubDate, out var dt) ? dt : DateTime.UtcNow,
            DateRange     = ExtractDateRange(combined)
        };
    }

    private static string? ExtractTag(string content, string tag)
    {
        var m = Regex.Match(content,
            $@"<{tag}[^>]*>(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?</{tag}>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? ExtractDateRange(string text)
    {
        var m = DateRangeRegex.Match(text);
        return m.Success ? m.Value.Trim() : null;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Regex.Replace(html, "<[^>]*>", "");
        return text
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ")
            .Trim();
    }
}
