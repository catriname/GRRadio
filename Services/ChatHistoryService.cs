using GRRadio.Models;
using System.Text.Json;

namespace GRRadio.Services;

public class ChatHistoryService
{
    private const int    MaxMessages = 500;
    private const string FileName    = "chat_history.json";

    private List<AprsMessage>? _cache;

    private string FilePath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    public List<AprsMessage> Load()
    {
        if (_cache is not null) return _cache;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                _cache = JsonSerializer.Deserialize<List<AprsMessage>>(json) ?? [];
            }
        }
        catch { }

        _cache ??= [];
        return _cache;
    }

    public void Add(AprsMessage msg)
    {
        var list = Load();
        list.Add(msg);

        if (list.Count > MaxMessages)
            list.RemoveRange(0, list.Count - MaxMessages);

        Persist(list);
    }

    public void Clear()
    {
        _cache = [];
        Persist(_cache);
    }

    private void Persist(List<AprsMessage> list)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch { }
    }
}
