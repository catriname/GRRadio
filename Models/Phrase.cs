namespace GRRadio.Models;

public class Phrase
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    // general | wellness | dx | morning | evening
    public string Category { get; set; } = "general";
    public bool IsUserAdded { get; set; } = false;
}
