using System.Text.Json;
using System.Text.Json.Serialization;
using GRRadio.Models;

namespace GRRadio.Services;

public class PhraseService
{
    private const string UserPhrasesKey = "grradio_user_phrases";
    private List<Phrase>? _builtIn;

    public async Task<List<Phrase>> GetAllPhrasesAsync()
    {
        var builtIn = await GetBuiltInPhrasesAsync();
        var user = GetUserPhrases();
        return [.. builtIn, .. user];
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
            Id = Guid.NewGuid().ToString(),
            Text = text.Trim(),
            Category = category,
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

    private async Task<List<Phrase>> GetBuiltInPhrasesAsync()
    {
        if (_builtIn is not null) return _builtIn;

        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync("phrases.json");
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            var doc = JsonSerializer.Deserialize<PhrasesDocument>(json, _jsonOpts);
            _builtIn = doc?.Phrases ?? [];
        }
        catch
        {
            _builtIn = [];
        }

        return _builtIn;
    }

    private sealed class PhrasesDocument
    {
        [JsonPropertyName("phrases")]
        public List<Phrase> Phrases { get; set; } = [];
    }
}
