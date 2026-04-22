namespace GRRadio.Models;

public static class AprsSymbols
{
    private static readonly Dictionary<char, (string Emoji, string Name)> Primary = new()
    {
        ['!'] = ("🚔", "Police Car"),
        ['$'] = ("📱", "Phone"),
        ['&'] = ("🌐", "Gateway"),
        ['\''] = ("✈️", "Small Aircraft"),
        ['*'] = ("❄️", "Snowflake"),
        ['+'] = ("✝️", "Church"),
        ['-'] = ("🏠", "House"),
        ['/'] = ("📍", "Dot"),
        ['>'] = ("🚗", "Car"),
        ['<'] = ("🏍️", "Motorcycle"),
        ['='] = ("🚂", "Train"),
        ['?'] = ("💻", "File Server"),
        ['@'] = ("🌀", "Hurricane"),
        ['A'] = ("➕", "Aid Station"),
        ['B'] = ("💾", "BBS"),
        ['D'] = ("🔁", "Digipeater"),
        ['H'] = ("🏨", "Hotel"),
        ['K'] = ("🏫", "School"),
        ['L'] = ("💡", "Lighthouse"),
        ['O'] = ("🎈", "Balloon"),
        ['R'] = ("🏕️", "RV"),
        ['S'] = ("🚀", "Shuttle"),
        ['T'] = ("📺", "SSTV"),
        ['U'] = ("🚌", "Bus"),
        ['X'] = ("🚁", "Helicopter"),
        ['Y'] = ("⛵", "Yacht"),
        ['['] = ("👤", "Person"),
        ['^'] = ("✈️", "Aircraft"),
        ['_'] = ("🌡️", "WX Station"),
        ['`'] = ("📡", "Dish"),
        ['a'] = ("🚑", "Ambulance"),
        ['b'] = ("🚲", "Bicycle"),
        ['d'] = ("🔥", "Fire Dept"),
        ['e'] = ("🐎", "Horse"),
        ['f'] = ("🚒", "Fire Truck"),
        ['g'] = ("🪂", "Glider"),
        ['h'] = ("🏥", "Hospital"),
        ['j'] = ("🚙", "Jeep"),
        ['k'] = ("🚛", "Truck"),
        ['n'] = ("🌐", "Node"),
        ['p'] = ("🐕", "Dog"),
        ['r'] = ("🗼", "Antenna"),
        ['s'] = ("🚤", "Boat"),
        [';'] = ("⛺", "Tent"),
        ['u'] = ("🚐", "Van"),
        ['v'] = ("🚐", "Van"),
        ['w'] = ("💧", "Water Station"),
        ['y'] = ("📶", "Yagi"),
    };

    public static string GetEmoji(char table, char code)
    {
        if (table == '/' && Primary.TryGetValue(code, out var s)) return s.Emoji;
        return "📍";
    }

    public static string GetEmoji(string code) =>
        code.Length == 2 ? GetEmoji(code[0], code[1]) : "📍";

    public static string GetName(char table, char code)
    {
        if (table == '/' && Primary.TryGetValue(code, out var s)) return s.Name;
        return "Station";
    }

    public static string GetName(string code) =>
        code.Length == 2 ? GetName(code[0], code[1]) : "Station";

    public static readonly (string Code, string Emoji, string Name)[] PickerSymbols =
    [
        ("/-", "🏠", "House"),
        ("/>", "🚗", "Car"),
        ("/[", "👤", "Person"),
        ("/$", "📱", "Phone"),
        ("/b", "🚲", "Bicycle"),
        ("/k", "🚛", "Truck"),
        ("/u", "🚐", "Van"),
        ("/<", "🏍️", "Motorcycle"),
        ("/=", "🚂", "Train"),
        ("/j", "🚙", "Jeep"),
        ("/s", "🚤", "Boat"),
        ("/Y", "⛵", "Yacht"),
        ("/X", "🚁", "Helicopter"),
        ("/^", "✈️", "Aircraft"),
        ("/g", "🪂", "Glider"),
        ("/R", "🏕️", "RV"),
        ("/U", "🚌", "Bus"),
        ("/O", "🎈", "Balloon"),
        ("/_", "🌡️", "WX Station"),
        ("/a", "🚑", "Ambulance"),
        ("/f", "🚒", "Fire Truck"),
        ("/r", "🗼", "Antenna"),
        ("/D", "🔁", "Digipeater"),
        ("/;", "⛺", "Tent"),
        ("/*", "❄️", "Snowflake"),
    ];
}
