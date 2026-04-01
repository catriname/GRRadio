namespace GRRadio.Models;

public class RedditPost
{
    public string Title { get; set; } = string.Empty;
    public string Subreddit { get; set; } = string.Empty;
    public string Permalink { get; set; } = string.Empty;
    public int Score { get; set; }
    public int CommentCount { get; set; }
    public string Author { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public string? Flair { get; set; }

    public string TimeAgo
    {
        get
        {
            var ago = DateTime.UtcNow - CreatedUtc;
            if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes}m";
            if (ago.TotalHours < 24) return $"{(int)ago.TotalHours}h";
            return $"{(int)ago.TotalDays}d";
        }
    }
}

public class DxNewsItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime PublishedDate { get; set; }
    public string? DateRange { get; set; }
    public bool HasDateRange => !string.IsNullOrEmpty(DateRange);
    public string DateLabel => PublishedDate.ToString("MMM d, yyyy");
    public string ShortTitle => Title.Contains(" - ")
        ? Title[..Title.IndexOf(" - ")].Trim()
        : Title.Length > 12 ? Title[..12] : Title;
}
