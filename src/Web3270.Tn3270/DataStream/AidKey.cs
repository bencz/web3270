namespace Web3270.Tn3270.DataStream;

/// <summary>
/// 3270 Attention Identifier (AID) bytes that the terminal sends back to
/// the host when an action key is pressed.
/// </summary>
public static class AidKey
{
    public const byte NoAid = 0x60;
    public const byte StructuredField = 0x88; // Inbound structured-field marker (Query Reply)
    public const byte Enter = 0x7D;
    public const byte Clear = 0x6D;
    public const byte SysReq = 0xF0;

    public const byte PA1 = 0x6C;
    public const byte PA2 = 0x6E;
    public const byte PA3 = 0x6B;

    public const byte PF1 = 0xF1;
    public const byte PF2 = 0xF2;
    public const byte PF3 = 0xF3;
    public const byte PF4 = 0xF4;
    public const byte PF5 = 0xF5;
    public const byte PF6 = 0xF6;
    public const byte PF7 = 0xF7;
    public const byte PF8 = 0xF8;
    public const byte PF9 = 0xF9;
    public const byte PF10 = 0x7A;
    public const byte PF11 = 0x7B;
    public const byte PF12 = 0x7C;
    public const byte PF13 = 0xC1;
    public const byte PF14 = 0xC2;
    public const byte PF15 = 0xC3;
    public const byte PF16 = 0xC4;
    public const byte PF17 = 0xC5;
    public const byte PF18 = 0xC6;
    public const byte PF19 = 0xC7;
    public const byte PF20 = 0xC8;
    public const byte PF21 = 0xC9;
    public const byte PF22 = 0x4A;
    public const byte PF23 = 0x4B;
    public const byte PF24 = 0x4C;

    public static byte ForName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return Enter;
        var n = name.ToUpperInvariant();
        return n switch
        {
            "ENTER" => Enter,
            "CLEAR" => Clear,
            "SYSREQ" => SysReq,
            "PA1" => PA1, "PA2" => PA2, "PA3" => PA3,
            "PF1" => PF1, "PF2" => PF2, "PF3" => PF3, "PF4" => PF4,
            "PF5" => PF5, "PF6" => PF6, "PF7" => PF7, "PF8" => PF8,
            "PF9" => PF9, "PF10" => PF10, "PF11" => PF11, "PF12" => PF12,
            "PF13" => PF13, "PF14" => PF14, "PF15" => PF15, "PF16" => PF16,
            "PF17" => PF17, "PF18" => PF18, "PF19" => PF19, "PF20" => PF20,
            "PF21" => PF21, "PF22" => PF22, "PF23" => PF23, "PF24" => PF24,
            _ => Enter
        };
    }

    /// <summary>True for AID keys that imply a "short read" — terminal must
    /// not send modified field data (Clear, PA1-PA3, SysReq).</summary>
    public static bool IsShortRead(byte aid) 
        => aid is Clear or PA1 or PA2 or PA3 or SysReq;
}