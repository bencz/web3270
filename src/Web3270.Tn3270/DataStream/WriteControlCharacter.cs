namespace Web3270.Tn3270.DataStream;

/// <summary>
/// Decoded view over the Write Control Character that follows every W/EW/EWA
/// command (3270 architecture, sec. 5.2).
/// </summary>
public readonly record struct WriteControlCharacter(byte Raw)
{
    public bool ResetMdt => (Raw & 0x01) != 0; // bit 7 (LSB)
    public bool KeyboardRestore => (Raw & 0x02) != 0; // bit 6
    public bool SoundAlarm => (Raw & 0x04) != 0; // bit 5
    public bool StartPrinter => (Raw & 0x08) != 0; // bit 4
    public byte PrinterFormat => (byte)((Raw & 0x30) >> 4); // bits 2-3
}