namespace Web3270.Tn3270.Telnet;

/// <summary>
/// TN3270E sub-negotiation constants (RFC 2355 §3.6 + §3.7).
/// Values follow the same numeric layout dm3270 / x3270 / wc3270 emit.
/// </summary>
public static class Tn3270ESub
{
    public const byte Associate = 0x00;
    public const byte Connect = 0x01;
    public const byte DeviceType = 0x02;
    public const byte Functions = 0x03;
    public const byte Is = 0x04;
    public const byte Reason = 0x05;
    public const byte Reject = 0x06;
    public const byte Request = 0x07;
    public const byte Send = 0x08;
}

/// <summary>Function codes negotiated under TN3270E FUNCTIONS subcommand.</summary>
public static class Tn3270EFunctions
{
    public const byte BindImage = 0x00;
    public const byte DataStreamCtl = 0x01;
    public const byte Responses = 0x02;
    public const byte ScsCtlCodes = 0x03;
    public const byte SysReq = 0x04;
}

/// <summary>Command header data type byte (RFC 2355 §3.5.1).</summary>
public static class Tn3270EDataType
{
    public const byte ThreeTwoSeventyData = 0x00;
    public const byte ScsData = 0x01;
    public const byte Response = 0x02;
    public const byte BindImage = 0x03;
    public const byte Unbind = 0x04;
    public const byte NvtData = 0x05;
    public const byte Request = 0x06;
    public const byte SsCpLuData = 0x07;
    public const byte PrintEoj = 0x08;
}