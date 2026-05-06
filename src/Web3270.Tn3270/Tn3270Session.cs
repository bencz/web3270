using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Web3270.Tn3270.DataStream;
using Web3270.Tn3270.Screen;
using Web3270.Tn3270.Telnet;

namespace Web3270.Tn3270;

/// <summary>
/// High-level façade for a single TN3270 conversation with a host.
/// Owns the TCP connection, the screen buffer and the protocol pipeline.
/// </summary>
public sealed class Tn3270Session : IAsyncDisposable
{
    private readonly Tn3270SessionOptions _options;
    private readonly ILogger<Tn3270Session> _log;
    private readonly IStreamRecorder _recorder;
    private TcpClient _tcp;
    private TelnetTransport _telnet;
    private Tn3270DataStreamParser _parser;
    private Tn3270OutboundBuilder _outbound;
    private Task _reader;
    private CancellationTokenSource _cts;
    private bool _disposed;

    public Tn3270Session(
        Tn3270SessionOptions options,
        ILogger<Tn3270Session> logger = null,
        IStreamRecorder recorder = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _log = logger ?? NullLogger<Tn3270Session>.Instance;
        _recorder = recorder;
        Buffer = new ScreenBuffer(options.Rows, options.Columns);
    }

    public ScreenBuffer Buffer { get; }
    public bool IsConnected => _tcp is { Connected: true };

    public event Func<Tn3270Session, Task> ScreenUpdated;
    public event Func<Tn3270Session, Exception, Task> Disconnected;

    public async Task ConnectAsync(CancellationToken ct)
    {
        if (IsConnected)
            throw new InvalidOperationException("Already connected");

        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_options.Host, _options.Port, ct).ConfigureAwait(false);
        _telnet = new TelnetTransport(_tcp, _options.TerminalType, _log, _recorder);
        _parser = new Tn3270DataStreamParser(Buffer, _log);
        _outbound = new Tn3270OutboundBuilder(Buffer);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _recorder?.Annotate(
            $"connect {_options.Host}:{_options.Port} terminal={_options.TerminalType} {_options.Rows}x{_options.Columns}");
        _reader = Task.Run(() => ReadLoopAsync(_cts.Token), CancellationToken.None);
        _log.SessionConnected(_options.Host, _options.Port);
    }

    public async Task SendAidAsync(byte aid, CancellationToken ct = default)
    {
        if (!IsConnected) 
            throw new InvalidOperationException("Not connected");
        _recorder?.Annotate($"AID 0x{aid:X2}");
        var record = _outbound.BuildReadModified(aid);
        await _telnet.WriteRecordAsync(record, ct).ConfigureAwait(false);
        Buffer.KeyboardLocked = true;
    }

    public bool TypeCharacter(char ch)
    {
        if (Buffer.KeyboardLocked) 
            return false;
        var addr = Buffer.CursorAddress;
        var field = Buffer.FieldAt(addr);
        if (field is null || field.Protected) 
            return false;
        if (field.Numeric && !IsNumericInputChar(ch))
            return false;

        var cell = Buffer[addr];
        if (cell.IsFieldAttribute)
            return false;
        cell.Character = Encoding.Ebcdic.FromUnicode(ch);
        cell.Modified = true;

        var attrCell = Buffer[field.AttributePosition];
        attrCell.Attribute = attrCell.Attribute.WithModified(true);

        Buffer.CursorAddress = Buffer.Wrap(addr + 1);
        return true;
    }

    public void MoveCursor(int newAddress) 
        => Buffer.CursorAddress = Buffer.Wrap(newAddress);

    public void Backspace()
    {
        var addr = Buffer.CursorAddress;
        var prev = Buffer.Wrap(addr - 1);
        var field = Buffer.FieldAt(prev);
        if (field is null || field.Protected)
            return;
        var cell = Buffer[prev];
        if (cell.IsFieldAttribute)
            return;
        cell.Character = 0x00;
        cell.Modified = true;
        var attrCell = Buffer[field.AttributePosition];
        attrCell.Attribute = attrCell.Attribute.WithModified(true);
        Buffer.CursorAddress = prev;
    }

    public void TabToNextField()
    {
        var fields = Buffer.Fields();
        if (fields.Count == 0) 
            return;
        var addr = Buffer.CursorAddress;
        for (var step = 1; step <= Buffer.Size; step++)
        {
            var probe = Buffer.Wrap(addr + step);
            var f = Buffer.FieldAt(probe);
            if (f is { Protected: false } && probe == f.Start)
            {
                Buffer.CursorAddress = probe;
                return;
            }
        }
    }

    private static bool IsNumericInputChar(char ch)
    {
        return ch is (>= '0' and <= '9') or '-' or '+' or '.' or ',';
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var record = await _telnet.ReadRecordAsync(ct).ConfigureAwait(false);
                if (record.Length == 0) 
                    continue;

                _parser.Process(record);
                Buffer.KeyboardLocked = _parser.LastWcc?.KeyboardRestore == false;

                // Some host commands (Read Partition Query, Read Buffer/Modified)
                // require an immediate inbound from us. The parser stages the
                // payload; we ship it back without involving the user.
                var reply = _parser.PendingReply;
                if (reply is not null)
                {
                    _recorder?.Annotate("auto-reply (Query Reply / Read response)");
                    await _telnet.WriteRecordAsync(reply, ct).ConfigureAwait(false);
                }

                if (ScreenUpdated is not null) 
                    await ScreenUpdated(this).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException ex)
        {
            await ReportDisconnectAsync(ex).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            await ReportDisconnectAsync(ex).ConfigureAwait(false);
        }
        catch (ObjectDisposedException ex)
        {
            await ReportDisconnectAsync(ex).ConfigureAwait(false);
        }
    }

    private async Task ReportDisconnectAsync(Exception ex)
    {
        _log.ReadLoopTerminated(ex);
        if (Disconnected is not null) 
            await Disconnected(this, ex).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_reader is not null)
            try
            {
                await _reader.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

        if (_telnet is not null) await _telnet.DisposeAsync().ConfigureAwait(false);
        try
        {
            _tcp?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        _cts?.Dispose();
    }
}

public sealed class Tn3270SessionOptions
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 23;
    public string TerminalType { get; init; } = "IBM-3278-2-E";
    public int Rows { get; init; } = 24;
    public int Columns { get; init; } = 80;

    /// <summary>
    /// Optional path of a file the session will write a hex/ASCII trace to.
    /// When null, no trace file is created.
    /// </summary>
    public string TraceFile { get; init; }
}
