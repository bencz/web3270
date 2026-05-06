namespace Web3270.Tn3270.Telnet;

/// <summary>
/// Telnet option bytes used by 3270 sessions (RFC 1091, 1205, 2355).
/// </summary>
public static class TelnetOption
{
    public const byte Binary = 0; // RFC 856
    public const byte Echo = 1;
    public const byte SuppressGoAhead = 3;
    public const byte TerminalType = 24; // 0x18 — RFC 1091
    public const byte EndOfRecord = 25; // 0x19 — RFC 885 (required for TN3270)
    public const byte TN3270Regime = 29; // 0x1D — RFC 1041
    public const byte TN3270E = 40; // 0x28 — RFC 2355

    /// <summary>
    /// Some hosts (notably Hercules with certain configs) use 0x29 in place
    /// of the standard 3270-Regime option. We treat it as a synonym to keep
    /// connections alive.
    /// </summary>
    public const byte TN3270RegimeAlt = 41; // 0x29
}

/// <summary>
/// Subnegotiation values for Terminal-Type (RFC 1091).
/// </summary>
public static class TerminalTypeSub
{
    public const byte Is = 0;
    public const byte Send = 1;
}