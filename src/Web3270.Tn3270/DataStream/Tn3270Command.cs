namespace Web3270.Tn3270.DataStream;

/// <summary>
/// 3270 data stream commands (host -> terminal). Both EBCDIC and ASCII forms
/// are supported because TN3270 hosts may negotiate either.
/// </summary>
public static class Tn3270Command
{
    // EBCDIC encoding (most common over TN3270)
    public const byte Write = 0xF1; // W
    public const byte EraseWrite = 0xF5; // EW
    public const byte EraseWriteAlt = 0x7E; // EWA
    public const byte ReadBuffer = 0xF2; // RB
    public const byte ReadModified = 0xF6; // RM
    public const byte ReadModifiedAll = 0x6E; // RMA
    public const byte EraseAllUnprot = 0x6F; // EAU
    public const byte WriteStructured = 0xF3; // WSF

    /// <summary>
    /// Returns true if the byte represents any recognised host -> terminal
    /// command opcode. ASCII variants (1, 5, 7E…) are accepted as fallbacks.
    /// </summary>
    public static bool IsCommand(byte b)
    {
        return b switch
        {
            Write or EraseWrite or EraseWriteAlt or ReadBuffer or ReadModified
                or ReadModifiedAll or EraseAllUnprot or WriteStructured => true,
            0x01 or 0x05 or 0x06 or 0x0F or 0x11 => true, // ASCII forms
            _ => false
        };
    }
}

/// <summary>
/// 3270 buffer orders embedded in a data stream.
/// </summary>
public static class Tn3270Order
{
    public const byte SF = 0x1D; // Start Field
    public const byte SFE = 0x29; // Start Field Extended
    public const byte SBA = 0x11; // Set Buffer Address
    public const byte SA = 0x28; // Set Attribute
    public const byte MF = 0x2C; // Modify Field
    public const byte IC = 0x13; // Insert Cursor
    public const byte PT = 0x05; // Program Tab
    public const byte RA = 0x3C; // Repeat to Address
    public const byte EUA = 0x12; // Erase Unprotected to Address
    public const byte GE = 0x08; // Graphic Escape
}

/// <summary>
/// Format Control Orders — embedded inside a Write payload, each is
/// rendered as an EBCDIC space (0x40) per 3270 architecture. Without
/// special handling the parser would store the raw byte in the buffer,
/// leading to garbage characters appearing on screen for hosts that use
/// these as "soft blank" sentinels (TSO logon banners are full of them).
/// </summary>
public static class FormatControlOrder
{
    public const byte Null = 0x00;
    public const byte FormFeed = 0x0C;
    public const byte CarriageReturn = 0x0D;
    public const byte NewLine = 0x15;
    public const byte EndOfMedium = 0x19;
    public const byte Duplicate = 0x1C;
    public const byte FieldMark = 0x1E;
    public const byte Substitute = 0x3F;
    public const byte EightOnes = 0xFF;

    public static bool IsFormatControl(byte b)
    {
        return b switch
        {
            Null or FormFeed or CarriageReturn or NewLine or EndOfMedium
                or Duplicate or FieldMark or Substitute or EightOnes => true,
            _ => false
        };
    }
}