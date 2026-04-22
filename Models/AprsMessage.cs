namespace GRRadio.Models;

public enum AprsMessageType
{
    UserMessage,
    Beacon,
    SystemLog,
    CallsignInfo,
    Acknowledgment,
    Rejection
}

public class AprsMessage
{
    public string          From          { get; set; } = string.Empty;
    public string          To            { get; set; } = string.Empty;
    public string          Message       { get; set; } = string.Empty;
    public string          MessageNumber { get; set; } = string.Empty;
    public string          Symbol        { get; set; } = string.Empty;
    public DateTime        Timestamp     { get; set; } = DateTime.Now;
    public AprsMessageType MessageType   { get; set; } = AprsMessageType.UserMessage;
}
