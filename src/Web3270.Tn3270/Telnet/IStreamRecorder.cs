namespace Web3270.Tn3270.Telnet;

/// <summary>
/// Optional sink that records every TN3270 record exchanged on a session.
/// Useful for offline parser debugging — point an implementation at a file
/// and rerun the captured trace through the parser without touching the host.
/// </summary>
public interface IStreamRecorder : IAsyncDisposable
{
    void RecordIncoming(ReadOnlySpan<byte> data);
    void RecordOutgoing(ReadOnlySpan<byte> data);
    void Annotate(string note);
}