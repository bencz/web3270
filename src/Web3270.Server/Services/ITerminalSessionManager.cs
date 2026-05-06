using Web3270.Server.Models;
using Web3270.Tn3270;

namespace Web3270.Server.Services;

/// <summary>
/// Maps a SignalR connection-id to the TN3270 session that backs it. Sessions
/// are owned by the manager so that clients don't need to leak the underlying
/// objects across hub method invocations.
/// </summary>
public interface ITerminalSessionManager
{
    Task<Tn3270Session> StartAsync(
        string connectionId,
        ConnectRequest request,
        Func<Tn3270Session, Task> onUpdate,
        Func<Tn3270Session, Exception, Task> onDisconnect,
        CancellationToken ct);

    Tn3270Session Find(string connectionId);
    string FindTraceFile(string connectionId);
    Task StopAsync(string connectionId);
}