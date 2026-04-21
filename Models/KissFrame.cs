namespace GRRadio.Models;

public class KissFrame
{
    public const byte FEND  = 0xC0;
    public const byte FESC  = 0xDB;
    public const byte TFEND = 0xDC;
    public const byte TFESC = 0xDD;

    public const byte CMD_DATA        = 0x00;
    public const byte CMD_TXDELAY     = 0x01;
    public const byte CMD_P           = 0x02;
    public const byte CMD_SLOTTIME    = 0x03;
    public const byte CMD_TXTAIL      = 0x04;
    public const byte CMD_FULLDUPLEX  = 0x05;
    public const byte CMD_SETHARDWARE = 0x06;
    public const byte CMD_RETURN      = 0xFF;

    public byte   Port    { get; set; } = 0;
    public byte   Command { get; set; } = CMD_DATA;
    public byte[] Data    { get; set; } = Array.Empty<byte>();

    public byte[] ToBytes()
    {
        var result = new List<byte> { FEND };
        result.Add((byte)((Port << 4) | (Command & 0x0F)));

        foreach (var b in Data)
        {
            if (b == FEND)      { result.Add(FESC); result.Add(TFEND); }
            else if (b == FESC) { result.Add(FESC); result.Add(TFESC); }
            else                { result.Add(b); }
        }

        result.Add(FEND);
        return result.ToArray();
    }

    public static KissFrame? FromBytes(byte[] data)
    {
        if (data.Length < 3 || data[0] != FEND || data[^1] != FEND)
            return null;

        var frame   = new KissFrame();
        var cmdByte = data[1];
        frame.Port    = (byte)((cmdByte >> 4) & 0x0F);
        frame.Command = (byte)(cmdByte & 0x0F);

        var unescaped = new List<byte>();
        var escaping  = false;

        for (int i = 2; i < data.Length - 1; i++)
        {
            if (escaping)
            {
                unescaped.Add(data[i] == TFEND ? FEND : data[i] == TFESC ? FESC : data[i]);
                escaping = false;
            }
            else if (data[i] == FESC) { escaping = true; }
            else                      { unescaped.Add(data[i]); }
        }

        frame.Data = unescaped.ToArray();
        return frame;
    }
}
