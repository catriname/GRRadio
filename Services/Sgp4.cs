namespace GRRadio.Services;

public class TleParsed
{
    public string Name    { get; set; } = string.Empty;
    public int    NoradId { get; set; }
    public string Line1   { get; set; } = string.Empty;
    public string Line2   { get; set; } = string.Empty;

    public static TleParsed? Parse(string name, string line1, string line2)
    {
        try
        {
            return new TleParsed
            {
                Name    = name.Trim(),
                NoradId = int.Parse(line1[2..7].Trim()),
                Line1   = line1,
                Line2   = line2
            };
        }
        catch { return null; }
    }
}
