namespace Web3270.Tn3270.Telnet;

/// <summary>
/// Telnet IAC command bytes (RFC 854 / RFC 855).
/// </summary>
public static class TelnetCommand
{
    public const byte SE = 240; // 0xF0 — end of subnegotiation
    public const byte NOP = 241;
    public const byte DM = 242;
    public const byte BRK = 243;
    public const byte IP = 244;
    public const byte AO = 245;
    public const byte AYT = 246;
    public const byte EC = 247;
    public const byte EL = 248;
    public const byte GA = 249;
    public const byte SB = 250; // 0xFA — begin subnegotiation
    public const byte WILL = 251; // 0xFB
    public const byte WONT = 252; // 0xFC
    public const byte DO = 253; // 0xFD
    public const byte DONT = 254; // 0xFE
    public const byte IAC = 255; // 0xFF
    public const byte EOR = 239; // 0xEF — TN3270 end-of-record marker (per RFC 1205)
}