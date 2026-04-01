using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GRRadio.Models;

namespace GRRadio.Services;

public class NewsService(HttpClient http)
{
    private const string DxFeedUrl = "https://dxnews.com/rss.xml";

    private (List<RedditPost> Posts, DateTime FetchedAt) _redditCache;
    private (List<DxNewsItem> Items, DateTime FetchedAt) _dxCache;

    public async Task<List<RedditPost>> GetRedditPostsAsync(List<string> subreddits)
    {
        if (_redditCache.Posts.Count > 0 &&
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
            .OrderByDescending(p => p.Score)
            .ToList();

        _redditCache = (sorted, DateTime.UtcNow);
        return sorted;
    }

    public async Task<List<DxNewsItem>> GetDxNewsAsync()
    {
        if (_dxCache.Items.Count > 0 &&
            (DateTime.UtcNow - _dxCache.FetchedAt).TotalHours < 1)
            return _dxCache.Items;

        try
        {
            var xml = await http.GetStringAsync(DxFeedUrl);
            var items = ParseRss(xml);
            _dxCache = (items, DateTime.UtcNow);
            return items;
        }
        catch
        {
            return [];
        }
    }

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

    private static List<DxNewsItem> ParseRss(string xml)
    {
        var items = new List<DxNewsItem>();
        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var item in doc.Descendants("item").Take(20))
            {
                var pubDate = item.Element("pubDate")?.Value;
                items.Add(new DxNewsItem
                {
                    Title       = item.Element("title")?.Value ?? "",
                    Url         = item.Element("link")?.Value ?? "",
                    Description = StripHtml(item.Element("description")?.Value ?? ""),
                    PublishedDate = DateTime.TryParse(pubDate, out var dt) ? dt : DateTime.UtcNow
                });
            }
        }
        catch { }
        return items;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Regex.Replace(html, "<[^>]*>", "");
        text = text
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ");
        return text.Trim();
    }
}
