using Microsoft.Extensions.Logging;

namespace Web3270.Tn3270;

/// <summary>
/// Source-generated logger message delegates used across the protocol library.
/// Centralising them here keeps every call site as a strongly-typed extension
/// method and satisfies CA1848 / CA1873 (no boxing, no formatter evaluation
/// when the level is disabled).
/// </summary>
internal static partial class LogMessages
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "TN3270 session connected to {Host}:{Port}")]
    public static partial void SessionConnected(this ILogger logger, string host, int port);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "TN3270 read loop terminated")]
    public static partial void ReadLoopTerminated(this ILogger logger, Exception ex);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Debug,
        Message = "WSF received ({Length} bytes); not fully implemented")]
    public static partial void WsfReceived(this ILogger logger, int length);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Unknown 3270 command 0x{Command:X2}")]
    public static partial void UnknownCommand(this ILogger logger, byte command);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning,
        Message = "Unknown structured field type 0x{Type:X2}")]
    public static partial void UnknownStructuredField(this ILogger logger, byte type);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "Query Reply staged in response to host's Read Partition Query")]
    public static partial void QueryReplyDispatched(this ILogger logger);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Trace,
        Message = "Telnet -> verb=0x{Verb:X2} option=0x{Option:X2}")]
    public static partial void TelnetNegotiation(this ILogger logger, byte verb, byte option);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Trace,
        Message = "Telnet <- verb=0x{Verb:X2} option=0x{Option:X2}")]
    public static partial void TelnetIncomingNegotiation(this ILogger logger, byte verb, byte option);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Trace,
        Message = "Telnet <- subnegotiation option=0x{Option:X2} ({Length} bytes)")]
    public static partial void TelnetSubnegotiation(this ILogger logger, byte option, int length);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Debug,
        Message = "Telnet sent terminal-type IS \"{TerminalType}\"")]
    public static partial void TelnetSentTerminalType(this ILogger logger, string terminalType);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Debug,
        Message = "TN3270 record received ({Length} bytes): {HexDump}")]
    public static partial void RecordReceived(this ILogger logger, int length, string hexDump);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug,
        Message = "TN3270 record sent ({Length} bytes): {HexDump}")]
    public static partial void RecordSent(this ILogger logger, int length, string hexDump);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Debug,
        Message = "Telnet stream closed by host")]
    public static partial void TelnetClosedByHost(this ILogger logger);

    [LoggerMessage(EventId = 1300, Level = LogLevel.Debug,
        Message = "TN3270E sent DEVICE_TYPE REQUEST \"{TerminalType}\"")]
    public static partial void TelnetSentDeviceType(this ILogger logger, string terminalType);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Information,
        Message = "TN3270E negotiated device={Device} lu={Lu}")]
    public static partial void TelnetTn3270EDevice(this ILogger logger, string device, string lu);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Debug,
        Message = "TN3270E sent FUNCTIONS REQUEST")]
    public static partial void TelnetSentFunctionsRequest(this ILogger logger);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Information,
        Message = "TN3270E session ready — every record now carries a 5-byte header")]
    public static partial void TelnetTn3270EReady(this ILogger logger);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Warning,
        Message = "TN3270E DEVICE_TYPE was REJECTED by host")]
    public static partial void TelnetTn3270EReject(this ILogger logger);
}