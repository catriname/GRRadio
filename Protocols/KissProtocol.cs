using GRRadio.Models;
using System.Text;

namespace GRRadio.Protocols;

public static class KissProtocol
{
    public static readonly Guid SppUuid = new("00001101-0000-1000-8000-00805f9b34fb");

    public static KissFrame CreateMessageFrame(string fromCall, string toCall, string message, int messageNumber = 0)
    {
        var suffix  = messageNumber > 0 ? $"{{{messageNumber}}}" : string.Empty;
        var payload = $":{toCall.PadRight(9)}:{message}{suffix}";
        return new KissFrame { Port = 0, Command = KissFrame.CMD_DATA, Data = BuildAx25UIFrame(fromCall, "APZ123", "WIDE1-1,WIDE2-1", payload) };
    }

    public static KissFrame CreateStatusFrame(string fromCall, string statusText)
    {
        return new KissFrame { Port = 0, Command = KissFrame.CMD_DATA, Data = BuildAx25UIFrame(fromCall, "APZ123", "WIDE1-1,WIDE2-1", $">{statusText}") };
    }

    public static KissFrame CreateBeaconFrame(string fromCall, double latitude, double longitude, string symbol, string comment)
    {
        var lat     = FormatAprsLat(latitude);
        var lon     = FormatAprsLon(longitude);
        var payload = $"!{lat}/{lon}{symbol} {comment}";
        return new KissFrame { Port = 0, Command = KissFrame.CMD_DATA, Data = BuildAx25UIFrame(fromCall, "APZ123", "WIDE1-1,WIDE2-1", payload) };
    }

    public static AprsMessage? ParseAprsMessage(KissFrame frame)
    {
        if (frame.Command != KissFrame.CMD_DATA || frame.Data.Length == 0)
            return null;

        try
        {
            var ax25Info = ParseAx25UIFrame(frame.Data);
            if (ax25Info is not { } info) return null;

            var (fromCall, toCall, payload) = info;
            if (string.IsNullOrEmpty(payload)) return null;

            if (payload.StartsWith(':'))  return ParseDirectMessage(fromCall, payload);
            if (payload.StartsWith('!') || payload.StartsWith('=')) return ParsePositionBeacon(fromCall, payload);
            if (payload.StartsWith('>')) return ParseStatusMessage(fromCall, payload);
            if (payload[0] is '\'' or '`' || (byte)payload[0] is 0x1C or 0x1D) return ParseMicE(fromCall, payload);
            // Extended Mic-E variant (e.g. Kenwood with { prefix) — symbol table at byte 7
            if (payload.Length >= 8 && (payload[7] == '/' || payload[7] == '\\')) return ParseMicE(fromCall, payload);

            return new AprsMessage { From = fromCall, To = toCall, Message = payload, MessageType = AprsMessageType.SystemLog, Timestamp = DateTime.Now };
        }
        catch { return null; }
    }

    // ── Lat/Lon → APRS format ────────────────────────────────────────────────

    private static string FormatAprsLat(double lat)
    {
        var hemi = lat >= 0 ? 'N' : 'S';
        lat = Math.Abs(lat);
        var deg = (int)lat;
        var min = (lat - deg) * 60;
        return $"{deg:D2}{min:05.2f}{hemi}";
    }

    private static string FormatAprsLon(double lon)
    {
        var hemi = lon >= 0 ? 'E' : 'W';
        lon = Math.Abs(lon);
        var deg = (int)lon;
        var min = (lon - deg) * 60;
        return $"{deg:D3}{min:05.2f}{hemi}";
    }

    // ── AX.25 helpers ────────────────────────────────────────────────────────

    private static byte[] BuildAx25UIFrame(string source, string destination, string digipeaters, string info)
    {
        var frame = new List<byte>();
        frame.AddRange(EncodeCallsign(destination, false));
        frame.AddRange(EncodeCallsign(source, false));

        var digis = digipeaters.Split(',');
        for (int i = 0; i < digis.Length; i++)
            frame.AddRange(EncodeCallsign(digis[i].Trim(), i == digis.Length - 1));

        frame.Add(0x03); // UI frame
        frame.Add(0xF0); // no layer 3
        frame.AddRange(Encoding.ASCII.GetBytes(info));
        return frame.ToArray();
    }

    private static byte[] EncodeCallsign(string callsign, bool isLast)
    {
        var encoded = new byte[7];
        var parts   = callsign.Split('-');
        var call    = parts[0].PadRight(6)[..6];
        var ssid    = parts.Length > 1 && byte.TryParse(parts[1], out var s) ? s : (byte)0;

        for (int i = 0; i < 6; i++) encoded[i] = (byte)(call[i] << 1);
        encoded[6] = (byte)(0x60 | (ssid << 1));
        if (isLast) encoded[6] |= 0x01;
        return encoded;
    }

    private static (string source, string destination, string info)? ParseAx25UIFrame(byte[] frame)
    {
        if (frame.Length < 16) return null;
        try
        {
            int offset      = 0;
            var destination = DecodeCallsign(frame, offset); offset += 7;
            var source      = DecodeCallsign(frame, offset); offset += 7;

            while (offset < frame.Length - 1 && (frame[offset - 1] & 0x01) == 0)
            {
                offset += 7;
                if (offset >= frame.Length) return null;
            }

            if (offset + 2 >= frame.Length) return null;
            offset += 2;

            var infoBytes = new byte[frame.Length - offset];
            Array.Copy(frame, offset, infoBytes, 0, infoBytes.Length);
            return (source, destination, Encoding.ASCII.GetString(infoBytes));
        }
        catch { return null; }
    }

    private static string DecodeCallsign(byte[] frame, int offset)
    {
        var callBytes = new byte[6];
        for (int i = 0; i < 6; i++) callBytes[i] = (byte)(frame[offset + i] >> 1);
        var call = Encoding.ASCII.GetString(callBytes).Trim();
        var ssid = (frame[offset + 6] >> 1) & 0x0F;
        return ssid > 0 ? $"{call}-{ssid}" : call;
    }

    // ── APRS payload parsers ──────────────────────────────────────────────────

    private static AprsMessage? ParseDirectMessage(string from, string payload)
    {
        try
        {
            var content   = payload[1..];
            if (content.Length < 10) return null;
            var recipient = content[..9].Trim();
            var msgStart  = content.IndexOf(':', 9);
            if (msgStart == -1) return null;
            var text      = content[(msgStart + 1)..];
            var msgNumIdx = text.LastIndexOf('{');
            var msgNum    = string.Empty;
            if (msgNumIdx > 0) { msgNum = text[(msgNumIdx + 1)..].TrimEnd('}'); text = text[..msgNumIdx]; }
            return new AprsMessage { From = from, To = recipient, Message = text, MessageNumber = msgNum, MessageType = AprsMessageType.UserMessage, Timestamp = DateTime.Now };
        }
        catch { return null; }
    }

    private static AprsMessage ParsePositionBeacon(string from, string payload)
    {
        // !DDMM.MMH/DDDMM.MMHsc[comment]  — sym table at [9], sym code at [19]
        try
        {
            if (payload.Length >= 20)
            {
                var table   = payload[9];
                var code    = payload[19];
                var comment = payload.Length > 20 ? payload[20..].Trim() : "";
                var emoji   = AprsSymbols.GetEmoji(table, code);
                return new AprsMessage { From = from, To = "BEACON", Symbol = $"{table}{code}",
                    Message = string.IsNullOrEmpty(comment) ? emoji : $"{emoji} {comment}",
                    MessageType = AprsMessageType.Beacon, Timestamp = DateTime.Now };
            }
        }
        catch { }
        return new() { From = from, To = "BEACON", Message = payload, MessageType = AprsMessageType.Beacon, Timestamp = DateTime.Now };
    }

    private static AprsMessage ParseMicE(string from, string payload)
    {
        // Mic-E: bytes 6=sym code, 7=sym table, 8+=comment (may have extension prefix chars)
        try
        {
            if (payload.Length >= 8)
            {
                var code    = payload[6];
                var table   = payload[7];
                var emoji   = AprsSymbols.GetEmoji(table, code);
                var comment = payload.Length > 8 ? payload[8..] : "";
                // Strip Kenwood/Yaesu Mic-E extension marker chars and leading junk
                comment = comment.TrimStart('`', '\'', '"', '!').Trim('}').Trim();
                return new AprsMessage { From = from, To = "POSITION", Symbol = $"{table}{code}",
                    Message = string.IsNullOrEmpty(comment) ? emoji : $"{emoji} {comment}",
                    MessageType = AprsMessageType.Beacon, Timestamp = DateTime.Now };
            }
        }
        catch { }
        return new() { From = from, To = "BEACON", Message = payload, MessageType = AprsMessageType.Beacon, Timestamp = DateTime.Now };
    }

    private static AprsMessage ParseStatusMessage(string from, string payload) =>
        new() { From = from, To = "STATUS", Message = payload[1..], MessageType = AprsMessageType.CallsignInfo, Timestamp = DateTime.Now };
}
