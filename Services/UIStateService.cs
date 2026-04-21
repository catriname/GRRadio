namespace GRRadio.Services;

public class UIStateService
{
    private readonly Dictionary<string, bool> _states = [];

    public bool Get(string key, bool defaultValue = true) =>
        _states.TryGetValue(key, out var val) ? val : defaultValue;

    public void Toggle(string key, bool defaultValue = true) =>
        _states[key] = !Get(key, defaultValue);
}
