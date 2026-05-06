namespace Web3270.Tn3270.DataStream;

/// <summary>
/// 12-bit / 14-bit 3270 buffer addressing helpers.
/// Buffer addresses are linear positions into the screen buffer (row-major).
/// </summary>
public static class BufferAddress
{
    /// <summary>
    /// 12-bit address translation table (used for the 12/14-bit forms).
    /// The two high bits of each byte select the encoding, so the low 6
    /// bits carry the actual address bits.
    /// </summary>
    private static readonly byte[] _decode =
    [
        0x40, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7,
        0xC8, 0xC9, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F,
        0x50, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7,
        0xD8, 0xD9, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7,
        0xE8, 0xE9, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7,
        0xF8, 0xF9, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F
    ];

    public static int Decode(byte hi, byte lo)
    {
        var topBits = (hi & 0xC0) >> 6;
        if (topBits == 0)
            // 14-bit address: top 6 bits = lower 6 bits of hi, then full 8 bits of lo
            return ((hi & 0x3F) << 8) | lo;
        // 12-bit address: 6 bits from each byte
        return ((hi & 0x3F) << 6) | (lo & 0x3F);
    }

    public static (byte hi, byte lo) Encode12(int address)
    {
        var hi = _decode[(address >> 6) & 0x3F];
        var lo = _decode[address & 0x3F];
        return (hi, lo);
    }
}