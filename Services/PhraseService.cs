using System.Text.Json;
using System.Text.Json.Serialization;
using GRRadio.Models;

namespace GRRadio.Services;

public class PhraseService(IHttpClientFactory httpClientFactory)
{
    private const string RemoteUrl      = "https://k5grr.com/grradio/phrases.json";
    private const string UserPhrasesKey = "grradio_user_phrases";
    private List<Phrase>? _defaultPhrases;
    private DateTime _remoteFetchedAt = DateTime.MinValue;
    private DateTime _remoteFailedAt  = DateTime.MinValue;

    public async Task<List<Phrase>> GetAllPhrasesAsync()
    {
        var defaults = await GetDefaultPhrasesAsync();
        var user     = GetUserPhrases();
        return [.. defaults, .. user];
    }

    public async Task<Phrase> GetRandomPhraseAsync()
    {
        var all = await GetAllPhrasesAsync();
        if (all.Count == 0)
            return new Phrase { Text = "The ionosphere is waiting. Get on the air!", Category = "general" };

        var timeCategory = DateTime.Now.Hour switch
        {
            >= 5 and < 12  => "morning",
            >= 17 and < 21 => "evening",
            _              => null
        };

        var filtered = all.Where(p =>
            p.Category is "general" or "wellness" or "dx" ||
            (timeCategory != null && p.Category == timeCategory)
        ).ToList();

        var pool = filtered.Count > 0 ? filtered : all;
        return pool[Random.Shared.Next(pool.Count)];
    }

    public List<Phrase> GetUserPhrases()
    {
        var json = Preferences.Default.Get(UserPhrasesKey, "[]");
        return JsonSerializer.Deserialize<List<Phrase>>(json) ?? [];
    }

    public void AddUserPhrase(string text, string category = "general")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var phrases = GetUserPhrases();
        phrases.Add(new Phrase
        {
            Id         = Guid.NewGuid().ToString(),
            Text       = text.Trim(),
            Category   = category,
            IsUserAdded = true
        });
        SaveUserPhrases(phrases);
    }

    public void DeleteUserPhrase(string id)
    {
        var phrases = GetUserPhrases();
        phrases.RemoveAll(p => p.Id == id);
        SaveUserPhrases(phrases);
    }

    private void SaveUserPhrases(List<Phrase> phrases) =>
        Preferences.Default.Set(UserPhrasesKey, JsonSerializer.Serialize(phrases));

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task<List<Phrase>> GetDefaultPhrasesAsync()
    {
        var now = DateTime.UtcNow;

        if (_defaultPhrases is not null && (now - _remoteFetchedAt).TotalHours < 12)
            return _defaultPhrases;

        if ((now - _remoteFailedAt).TotalMinutes < 30)
            return _defaultPhrases ?? await LoadBundledPhrasesAsync();

        var remote = await FetchRemotePhrasesAsync();
        if (remote is not null)
        {
            _defaultPhrases  = remote;
            _remoteFetchedAt = now;
            return _defaultPhrases;
        }

        _remoteFailedAt = now;
        return _defaultPhrases ?? await LoadBundledPhrasesAsync();
    }

    private async Task<List<Phrase>?> FetchRemotePhrasesAsync()
    {
        try
        {
            var client = httpClientFactory.CreateClient("phrases");
            var json   = await client.GetStringAsync(RemoteUrl);
            var doc    = JsonSerializer.Deserialize<PhrasesDocument>(json, _jsonOpts);
            return doc?.Phrases is { Count: > 0 } list ? list : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<List<Phrase>> LoadBundledPhrasesAsync()
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("phrases.json");
            using var reader       = new StreamReader(stream);
            var json               = await reader.ReadToEndAsync();
            var doc                = JsonSerializer.Deserialize<PhrasesDocument>(json, _jsonOpts);
            return doc?.Phrases ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class PhrasesDocument
    {
        [JsonPropertyName("phrases")]
        public List<Phrase> Phrases { get; set; } = [];
    }
}
