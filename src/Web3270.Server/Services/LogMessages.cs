namespace Web3270.Server.Services;

internal static partial class LogMessages
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Session started for {ConnectionId}")]
    public static partial void SessionStarted(this ILogger logger, string connectionId);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Session stopped for {ConnectionId}")]
    public static partial void SessionStopped(this ILogger logger, string connectionId);

    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning, Message = "Connect failed for {ConnectionId}")]
    public static partial void ConnectFailed(this ILogger logger, string connectionId, Exception ex);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Session trace file: {Path}")]
    public static partial void SessionTraceFile(this ILogger logger, string path);

    [LoggerMessage(EventId = 2200, Level = LogLevel.Debug,
        Message = "Hub <- {ConnectionId}: kind={Kind} value={Value} address={Address}")]
    public static partial void HubKeyReceived(this ILogger logger, string connectionId, string kind, string value,
        int? address);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Debug,
        Message = "Hub state {ConnectionId}: cursor={Cursor} keyboardLocked={Locked}")]
    public static partial void HubBufferState(this ILogger logger, string connectionId, int cursor, bool locked);
}