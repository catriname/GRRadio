using GRRadio.Models;
using System.Text.RegularExpressions;

namespace GRRadio.Services;

public class ClassifiedService(IHttpClientFactory httpFactory)
{
    // QTH date-search URL — returns today's new listings
    private static string QthUrl =>
        $"https://swap.qth.com/search-results.php?fieldtosearch=DatePlaced&keywords={DateTime.Now:MM/dd/yy}";

    private static string QthYesterdayUrl =>
        $"https://swap.qth.com/search-results.php?fieldtosearch=DatePlaced&keywords={DateTime.Now.AddDays(-1):MM/dd/yy}";

    private const string QrzSwapUrl = "https://www.qrz.com/page/hotswap.html";

    private (List<ClassifiedListing> Items, DateTime FetchedAt) _cache;

    public async Task<List<ClassifiedListing>> GetListingsAsync()
    {
        if (_cache.Items != null && _cache.Items.Count > 0 &&
            (DateTime.UtcNow - _cache.FetchedAt).TotalMinutes < 15)
            return _cache.Items;

        var qthItems = new List<ClassifiedListing>();
        var qrzItems = new List<ClassifiedListing>();

        try { qthItems = await FetchQthAsync();  } catch { }
        try { qrzItems = await FetchQrzAsync();  } catch { }

        var all = Interleave(qthItems.Take(20).ToList(), qrzItems.Take(20).ToList());
        _cache = (all, DateTime.UtcNow);
        return all;
    }

    private static List<T> Interleave<T>(params List<T>[] lists)
    {
        var result = new List<T>();
        int max = lists.Max(l => l.Count);
        for (int i = 0; i < max; i++)
            foreach (var list in lists)
                if (i < list.Count) result.Add(list[i]);
        return result;
    }

    // ── QTH swap.qth.com ──────────────────────────────────────────
    // Structure: <b>TITLE</b> … Listing #ID - Submitted on MM/DD/YY

    private async Task<List<ClassifiedListing>> FetchQthAsync()
    {
        using var http = httpFactory.CreateClient("classified");
        var html  = await http.GetStringAsync(QthUrl);
        var items = ParseQthHtml(html);

        if (items.Count == 0)
        {
            html  = await http.GetStringAsync(QthYesterdayUrl);
            items = ParseQthHtml(html);
        }

        return items;
    }

    private static List<ClassifiedListing> ParseQthHtml(string html)
    {
        var listings = new List<ClassifiedListing>();

        var matches = Regex.Matches(html,
            @"<b[^>]*>(.*?)</b>(.{0,1500}?)Listing\s*#(\d+)\s*-\s*Submitted on\s*([\d/]+)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in matches.Take(25))
        {
            var rawTitle = StripHtml(m.Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(rawTitle) || rawTitle.Length < 4) continue;
            if (rawTitle.Contains('{') || rawTitle.Contains('\n') || rawTitle.Length > 200) continue;

            var title = Regex.Replace(rawTitle, @"^[A-Z0-9]+\s*-\s*", "").Trim();
            if (string.IsNullOrWhiteSpace(title)) title = rawTitle;

            var counter = m.Groups[3].Value;
            var date    = m.Groups[4].Value.Trim();
            var url     = $"https://swap.qth.com/view_ad.php?counter={counter}";

            var snippet = StripHtml(m.Groups[2].Value).Trim();
            snippet = Regex.Replace(snippet, @"\s+", " ").Trim();
            if (snippet.Length > 120) snippet = snippet[..120];

            listings.Add(new ClassifiedListing
            {
                Title   = title,
                Url     = url,
                TimeAgo = date,
                Snippet = snippet,
                Source  = "QTH"
            });
        }

        return listings.DistinctBy(l => l.Url).Take(20).ToList();
    }

    // ── QRZ Hot Swap qrz.com/page/hotswap.html ────────────────────
    // Structure: <table class="slt"> rows with <a href> title and date <td>

    private async Task<List<ClassifiedListing>> FetchQrzAsync()
    {
        using var http = httpFactory.CreateClient("classified");
        var html = await http.GetStringAsync(QrzSwapUrl);
        return ParseQrzSwapHtml(html);
    }

    private static List<ClassifiedListing> ParseQrzSwapHtml(string html)
    {
        var listings = new List<ClassifiedListing>();

        // Swapmeet listing anchors have class="fb"; date follows in the next <td>
        var matches = Regex.Matches(html,
            @"<a\b(?=[^>]*\bclass=""fb"")[^>]*href=""(https://forums\.qrz\.com/[^""]+)""[^>]*>(.*?)</a>" +
            @"\s*</td>\s*<td[^>]*>(.*?)</td>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match m in matches.Take(25))
        {
            var url      = m.Groups[1].Value.Trim();
            var rawTitle = StripHtml(m.Groups[2].Value).Trim();
            if (string.IsNullOrWhiteSpace(rawTitle) || rawTitle.Length < 4) continue;

            var (title, snippet) = ParseQrzTitle(rawTitle);

            var timeAgo = "";
            if (DateTime.TryParse(StripHtml(m.Groups[3].Value).Trim(), out var dt))
                timeAgo = FormatRelativeDate(dt);

            listings.Add(new ClassifiedListing
            {
                Title   = title,
                Url     = url,
                TimeAgo = timeAgo,
                Snippet = snippet,
                Source  = "QRZ"
            });
        }

        return listings.DistinctBy(l => l.Url).Take(20).ToList();
    }

    // Parse "FS: Title - $500" → ("Title - $500", "For Sale · $500")
    private static (string Title, string Snippet) ParseQrzTitle(string raw)
    {
        var typeMatch = Regex.Match(raw,
            @"^\s*\[?(FS|WTB|WTT|ISO|SOLD|F\/S)\]?\s*[:\-]?\s*",
            RegexOptions.IgnoreCase);

        var label = "";
        var clean = raw;
        if (typeMatch.Success)
        {
            label = typeMatch.Groups[1].Value.ToUpperInvariant() switch
            {
                "FS" or "F/S" => "For Sale",
                "WTB" or "ISO" => "Wanted to Buy",
                "WTT"          => "Want to Trade",
                "SOLD"         => "Sold",
                _              => typeMatch.Groups[1].Value.ToUpperInvariant()
            };
            clean = raw[typeMatch.Length..].Trim().TrimStart('-', ':').Trim();
            if (string.IsNullOrWhiteSpace(clean)) clean = raw;
        }

        // Extract price anywhere in the original title
        var priceMatch = Regex.Match(raw, @"\$[\d,]+(?:\.\d{2})?");
        var price = priceMatch.Success ? priceMatch.Value : "";

        var snippet = string.IsNullOrEmpty(label) ? price
                    : string.IsNullOrEmpty(price)  ? label
                    : $"{label} · {price}";

        return (clean, snippet);
    }

    private static string FormatRelativeDate(DateTime dt)
    {
        var ago = DateTime.UtcNow - dt.ToUniversalTime();
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalHours   < 24) return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
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
