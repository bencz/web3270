using System.Globalization;
using System.Net.Sockets;
using SysEncoding = System.Text.Encoding;
using StringBuilder = System.Text.StringBuilder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Web3270.Tn3270.Telnet;

/// <summary>
/// Telnet/TN3270/TN3270E framing layer over a raw <see cref="NetworkStream"/>.
///
/// Owns: IAC negotiation, escaped 0xFF unwrapping, record boundary
/// detection (IAC EOR), TN3270E sub-negotiation (RFC 2355) and the
/// 5-byte TN3270E command header that wraps each application record
/// once TN3270E mode is active.
///
/// Real-world hosts (TSO/TK5 in particular) refuse to drive the TSO
/// command field through plain TN3270 mode in any predictable way —
/// IKT00405I screen-erasure recovery fires on every Read Modified
/// response. Switching to TN3270E with the same DEVICE_TYPE / FUNCTIONS
/// dance dm3270 and the Soldier-of-Fortran tn3270lib perform makes
/// VTAM happy and lets logon proceed normally.
/// </summary>
public sealed class TelnetTransport : IAsyncDisposable
{
    private const int ReadBufferSize = 4096;

    private static readonly byte[] FunctionsRequested =
    [
        Tn3270EFunctions.BindImage,
        Tn3270EFunctions.Responses,
        Tn3270EFunctions.SysReq
    ];

    private readonly TcpClient _tcp;
    private readonly NetworkStream _net;
    private readonly ILogger _log;
    private readonly IStreamRecorder _recorder;
    private readonly string _terminalType;
    private readonly byte[] _readBuffer = new byte[ReadBufferSize];
    private readonly List<byte> _record = new(1024);
    private bool _disposed;
    private int _outboundSequence;

    public TelnetTransport(
        TcpClient tcp,
        string terminalType,
        ILogger logger = null,
        IStreamRecorder recorder = null)
    {
        ArgumentNullException.ThrowIfNull(tcp);
        _tcp = tcp;
        _net = tcp.GetStream();
        _terminalType = terminalType ?? "IBM-3278-2-E";
        _log = logger ?? NullLogger.Instance;
        _recorder = recorder;
    }

    public bool BinaryMode { get; private set; }
    public bool EorMode { get; private set; }

    /// <summary>True once a successful TN3270E DEVICE_TYPE IS + FUNCTIONS IS round-trip
    /// has completed. While true every record carries a 5-byte header.</summary>
    private bool Tn3270EMode { get; set; }

    private string NegotiatedDeviceType { get; set; }
    private string LogicalUnitName { get; set; }

    /// <summary>
    /// Reads bytes off the wire until the next complete TN3270 record
    /// (terminated by IAC EOR) is available. Returns the record payload
    /// without the trailing IAC EOR and, when in TN3270E mode, without
    /// the leading 5-byte command header.
    /// </summary>
    public async Task<byte[]> ReadRecordAsync(CancellationToken ct)
    {
        while (true)
        {
            _record.Clear();
            while (true)
            {
                var read = await _net.ReadAsync(_readBuffer, ct).ConfigureAwait(false);
                if (read == 0)
                {
                    _log.TelnetClosedByHost();
                    throw new IOException("Telnet stream closed");
                }

                if (await ProcessIncomingAsync(_readBuffer, read, ct).ConfigureAwait(false)) 
                    break;
            }

            var raw = _record.ToArray();
            _recorder?.RecordIncoming(raw);
            if (_log.IsEnabled(LogLevel.Debug))
                _log.RecordReceived(raw.Length, HexDump(raw, raw.Length));

            // Strip the TN3270E 5-byte header (data-type, request-flag,
            // response-flag, seq-hi, seq-lo) before handing the bytes to
            // the upper-layer 3270 parser. If the data type isn't
            // 3270_DATA we drop the record (BIND_IMAGE / UNBIND / RESPONSE
            // are session-level and don't need parsing here).
            // If the host requested ALWAYS_RESPONSE we send back a
            // POSITIVE_RESPONSE with the same sequence number — VTAM
            // expects this acknowledgement and will treat the session as
            // hung otherwise.
            if (!Tn3270EMode)
                return raw;

            if (raw.Length < 5)
                continue;
            var dataType = raw[0];
            var responseFlag = raw[2];
            var seqHi = raw[3];
            var seqLo = raw[4];
            if (responseFlag == 0x02) // ALWAYS_RESPONSE
                await SendPositiveResponseAsync(seqHi, seqLo, ct).ConfigureAwait(false);
            if (dataType != Tn3270EDataType.ThreeTwoSeventyData)
                continue;
            var stripped = new byte[raw.Length - 5];
            Array.Copy(raw, 5, stripped, 0, stripped.Length);
            return stripped;
        }
    }

    /// <summary>
    /// Sends a TN3270 record to the host. In TN3270E mode the 5-byte
    /// command header is automatically prepended.
    /// </summary>
    public async Task WriteRecordAsync(byte[] payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var effective = payload;
        if (Tn3270EMode)
        {
            var hdr = new byte[5];
            hdr[0] = Tn3270EDataType.ThreeTwoSeventyData;
            hdr[1] = 0x00; // request flag
            hdr[2] = 0x00; // response flag (NO_RESPONSE)
            var seq = unchecked((ushort)Interlocked.Increment(ref _outboundSequence));
            hdr[3] = (byte)((seq >> 8) & 0xFF);
            hdr[4] = (byte)(seq & 0xFF);
            effective = new byte[hdr.Length + payload.Length];
            Array.Copy(hdr, 0, effective, 0, hdr.Length);
            Array.Copy(payload, 0, effective, hdr.Length, payload.Length);
        }

        var buffer = new List<byte>(effective.Length + 16);
        foreach (var b in effective)
        {
            buffer.Add(b);
            if (b == TelnetCommand.IAC) 
                buffer.Add(TelnetCommand.IAC);
        }

        buffer.Add(TelnetCommand.IAC);
        buffer.Add(TelnetCommand.EOR);

        var arr = buffer.ToArray();
        if (_log.IsEnabled(LogLevel.Debug))
            _log.RecordSent(arr.Length, HexDump(arr, arr.Length));
        _recorder?.RecordOutgoing(effective);
        await _net.WriteAsync(arr, ct).ConfigureAwait(false);
        await _net.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> ProcessIncomingAsync(byte[] chunk, int length, CancellationToken ct)
    {
        var i = 0;
        while (i < length)
        {
            var b = chunk[i];
            if (b != TelnetCommand.IAC)
            {
                _record.Add(b);
                i++;
                continue;
            }

            if (i + 1 >= length)
            {
                i++;
                continue;
            }

            var c = chunk[i + 1];

            if (c == TelnetCommand.IAC)
            {
                _record.Add(TelnetCommand.IAC);
                i += 2;
                continue;
            }

            if (c == TelnetCommand.EOR)
            {
                i += 2;
                if (_record.Count > 0)
                    return true;
                continue;
            }

            switch (c)
            {
                case TelnetCommand.WILL:
                case TelnetCommand.WONT:
                case TelnetCommand.DO:
                case TelnetCommand.DONT:
                    if (i + 2 >= length)
                        return false;
                    await HandleNegotiationAsync(c, chunk[i + 2], ct).ConfigureAwait(false);
                    i += 3;
                    break;
                case TelnetCommand.SB:
                    var endIdx = FindSubnegotiationEnd(chunk, i + 2, length);
                    if (endIdx < 0)
                        return false;
                    var payload = new byte[endIdx - (i + 2)];
                    Array.Copy(chunk, i + 2, payload, 0, payload.Length);
                    await HandleSubnegotiationAsync(payload, ct).ConfigureAwait(false);
                    i = endIdx + 2;
                    break;
                default:
                    i += 2;
                    break;
            }
        }

        return false;
    }

    private static int FindSubnegotiationEnd(byte[] data, int from, int length)
    {
        for (var i = from; i < length - 1; i++)
            if (data[i] == TelnetCommand.IAC && data[i + 1] == TelnetCommand.SE)
                return i;

        return -1;
    }

    private async Task HandleNegotiationAsync(byte verb, byte option, CancellationToken ct)
    {
        _log.TelnetIncomingNegotiation(verb, option);

        var accepted =
            option == TelnetOption.Binary ||
            option == TelnetOption.EndOfRecord ||
            option == TelnetOption.TerminalType ||
            option == TelnetOption.SuppressGoAhead ||
            option == TelnetOption.TN3270Regime ||
            option == TelnetOption.TN3270RegimeAlt ||
            option == TelnetOption.TN3270E;

        switch (option)
        {
            case TelnetOption.Binary:
                BinaryMode = true;
                break;
            case TelnetOption.EndOfRecord:
                EorMode = true;
                break;
        }

        if (accepted)
        {
            var response = (verb, option) switch
            {
                (TelnetCommand.DO, _) => TelnetCommand.WILL,
                (TelnetCommand.WILL, TelnetOption.TerminalType) => TelnetCommand.WONT,
                (TelnetCommand.WILL, _) => TelnetCommand.DO,
                (TelnetCommand.WONT, _) => TelnetCommand.DONT,
                _ => TelnetCommand.WONT
            };
            await SendResponseAsync(response, option, ct).ConfigureAwait(false);
            return;
        }

        var refusal = verb switch
        {
            TelnetCommand.DO => TelnetCommand.WONT,
            TelnetCommand.WILL => TelnetCommand.DONT,
            TelnetCommand.WONT => TelnetCommand.DONT,
            _ => TelnetCommand.WONT
        };
        await SendResponseAsync(refusal, option, ct).ConfigureAwait(false);
    }

    private async Task HandleSubnegotiationAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length == 0)
            return;

        _log.TelnetSubnegotiation(payload[0], payload.Length);

        switch (payload[0])
        {
            case TelnetOption.TerminalType:
                await HandleTerminalTypeSubnegotiationAsync(payload, ct).ConfigureAwait(false);
                return;
            case TelnetOption.TN3270E:
                await HandleTn3270ESubnegotiationAsync(payload, ct).ConfigureAwait(false);
                return;
        }
    }

    private async Task HandleTerminalTypeSubnegotiationAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length < 2 || payload[1] != TerminalTypeSub.Send)
            return;

        var name = SysEncoding.ASCII.GetBytes(_terminalType);
        var response = new List<byte>(name.Length + 6)
        {
            TelnetCommand.IAC, TelnetCommand.SB,
            TelnetOption.TerminalType, TerminalTypeSub.Is
        };
        response.AddRange(name);
        response.Add(TelnetCommand.IAC);
        response.Add(TelnetCommand.SE);
        await _net.WriteAsync(response.ToArray(), ct).ConfigureAwait(false);
        await _net.FlushAsync(ct).ConfigureAwait(false);
        _log.TelnetSentTerminalType(_terminalType);
    }

    private async Task HandleTn3270ESubnegotiationAsync(byte[] payload, CancellationToken ct)
    {
        // payload[0] = TN3270E option byte
        // payload[1] = main subcommand (SEND, DEVICE_TYPE, FUNCTIONS, ...)
        // payload[2..] = subcommand-specific
        if (payload.Length < 3) return;

        var sub = payload[1];

        switch (sub)
        {
            case Tn3270ESub.Send when payload[2] == Tn3270ESub.DeviceType:
            {
                // Host: SB TN3270E SEND DEVICE_TYPE IAC SE  -> we reply with REQUEST + name.
                var name = SysEncoding.ASCII.GetBytes(_terminalType);
                var response = new List<byte>(name.Length + 7)
                {
                    TelnetCommand.IAC, TelnetCommand.SB, TelnetOption.TN3270E,
                    Tn3270ESub.DeviceType, Tn3270ESub.Request
                };
                response.AddRange(name);
                response.Add(TelnetCommand.IAC);
                response.Add(TelnetCommand.SE);
                await _net.WriteAsync(response.ToArray(), ct).ConfigureAwait(false);
                await _net.FlushAsync(ct).ConfigureAwait(false);
                _log.TelnetSentDeviceType(_terminalType);
                return;
            }
            case Tn3270ESub.DeviceType when payload[2] == Tn3270ESub.Is:
            {
                // Host: SB TN3270E DEVICE_TYPE IS <type>[ 01 <lu>]? IAC SE
                // payload[3..] holds device type (and optionally CONNECT lu).
                var luSeparator = -1;
                for (var k = 3; k < payload.Length; k++)
                {
                    if (payload[k] != Tn3270ESub.Connect)
                        continue;

                    luSeparator = k;
                    break;
                }

                var deviceTypeEnd = luSeparator > 0 ? luSeparator : payload.Length;
                NegotiatedDeviceType = SysEncoding.ASCII.GetString(payload, 3, deviceTypeEnd - 3);
                if (luSeparator > 0)
                    LogicalUnitName = SysEncoding.ASCII.GetString(
                        payload, luSeparator + 1, payload.Length - luSeparator - 1);

                _log.TelnetTn3270EDevice(
                    NegotiatedDeviceType ?? string.Empty,
                    LogicalUnitName ?? string.Empty);

                // Now request the desired functions. dm3270 always asks for
                // BIND_IMAGE + RESPONSES + SYSREQ.
                var response = new List<byte>(FunctionsRequested.Length + 7)
                {
                    TelnetCommand.IAC, TelnetCommand.SB, TelnetOption.TN3270E,
                    Tn3270ESub.Functions, Tn3270ESub.Request
                };
                response.AddRange(FunctionsRequested);
                response.Add(TelnetCommand.IAC);
                response.Add(TelnetCommand.SE);
                await _net.WriteAsync(response.ToArray(), ct).ConfigureAwait(false);
                await _net.FlushAsync(ct).ConfigureAwait(false);
                _log.TelnetSentFunctionsRequest();
                return;
            }
            case Tn3270ESub.DeviceType when payload[2] == Tn3270ESub.Reject:
                _log.TelnetTn3270EReject();
                Tn3270EMode = false;
                return;
            case Tn3270ESub.Functions when payload[2] == Tn3270ESub.Is:
                // Host accepted (or counter-proposed) our function list.
                // Mark TN3270E mode active — every subsequent record will carry
                // a 5-byte header.
                Tn3270EMode = true;
                _log.TelnetTn3270EReady();
                return;
            case Tn3270ESub.Functions when payload[2] == Tn3270ESub.Request:
            {
                // Host counter-proposed a different function set. Echo it back
                // as IS to accept their proposal.
                var response = new List<byte>(payload.Length + 6)
                {
                    TelnetCommand.IAC, TelnetCommand.SB
                };
                response.AddRange(payload);
                response[5] = Tn3270ESub.Is; // replace REQUEST with IS at offset 4 of payload + 2 wrappers
                response.Add(TelnetCommand.IAC);
                response.Add(TelnetCommand.SE);
                await _net.WriteAsync(response.ToArray(), ct).ConfigureAwait(false);
                await _net.FlushAsync(ct).ConfigureAwait(false);
                Tn3270EMode = true;
                _log.TelnetTn3270EReady();
                break;
            }
        }
    }

    private static string HexDump(byte[] data, int length)
    {
        var max = Math.Min(length, 256);
        var sb = new StringBuilder(max * 3);
        for (var i = 0; i < max; i++)
        {
            if (i > 0)
                sb.Append(' ');
            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        if (length > max)
            sb.Append("...");
        return sb.ToString();
    }

    private async Task SendPositiveResponseAsync(byte seqHi, byte seqLo, CancellationToken ct)
    {
        // 5-byte header (RESPONSE / NO_RESPONSE / POSITIVE_RESPONSE / seq) +
        // 1-byte body (DEVICE_END = 0x00). Frame with IAC EOR. We bypass
        // WriteRecordAsync because that one prepends ITS OWN header.
        var record = new byte[]
        {
            Tn3270EDataType.Response, // data type = RESPONSE
            0x00, // request flag
            0x00, // response flag = POSITIVE_RESPONSE
            seqHi, seqLo, // mirror the host's sequence number
            0x00 // body: DEVICE_END
        };

        var framed = new List<byte>(record.Length + 8);
        foreach (var b in record)
        {
            framed.Add(b);
            if (b == TelnetCommand.IAC)
                framed.Add(TelnetCommand.IAC);
        }

        framed.Add(TelnetCommand.IAC);
        framed.Add(TelnetCommand.EOR);

        var arr = framed.ToArray();
        _recorder?.Annotate($"TN3270E POSITIVE_RESPONSE seq=0x{seqHi:X2}{seqLo:X2}");
        await _net.WriteAsync(arr, ct).ConfigureAwait(false);
        await _net.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task SendResponseAsync(byte verb, byte option, CancellationToken ct)
    {
        var buf = new[] { TelnetCommand.IAC, verb, option };
        await _net.WriteAsync(buf, ct).ConfigureAwait(false);
        await _net.FlushAsync(ct).ConfigureAwait(false);
        _log.TelnetNegotiation(verb, option);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            await _net.DisposeAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _tcp.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}