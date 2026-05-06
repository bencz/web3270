using System.Globalization;
using System.Text;

namespace Web3270.Tn3270.Telnet;

/// <summary>
/// Records inbound / outbound TN3270 records to a text file with a
/// <c>hexdump -C</c>-style layout. One file per session — the path is
/// supplied by the caller so the manager can name it after a connection-id.
/// </summary>
public sealed class FileStreamRecorder : IStreamRecorder
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public FileStreamRecorder(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Path = path;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
        WriteHeader();
    }

    public string Path { get; }

    private void WriteHeader()
    {
        _writer.WriteLine("# web3270 stream capture");
        _writer.WriteLine($"# started {DateTimeOffset.UtcNow:O}");
        _writer.WriteLine();
    }

    public void RecordIncoming(ReadOnlySpan<byte> data)
    {
        Append("<<", data);
    }

    public void RecordOutgoing(ReadOnlySpan<byte> data)
    {
        Append(">>", data);
    }

    public void Annotate(string note)
    {
        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _writer.WriteLine($"# {Timestamp()} {note}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Append(string direction, ReadOnlySpan<byte> data)
    {
        _gate.Wait();
        try
        {
            if (_disposed)
                return;
            _writer.WriteLine($"{Timestamp()} {direction} {data.Length} bytes");
            DumpHex(data);
            _writer.WriteLine();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void DumpHex(ReadOnlySpan<byte> data)
    {
        var line = new StringBuilder(80);
        var ascii = new StringBuilder(16);
        for (var i = 0; i < data.Length; i++)
        {
            if (i % 16 == 0)
            {
                if (i > 0)
                    FlushLine(line, ascii);
                line.Append(i.ToString("X8", CultureInfo.InvariantCulture)).Append("  ");
            }
            else if (i % 8 == 0)
            {
                line.Append(' ');
            }

            line.Append(data[i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            var c = data[i];
            ascii.Append(c is >= 0x20 and < 0x7F
                ? (char)c
                : '.');
        }

        if (line.Length > 0)
            FlushLine(line, ascii);
    }

    private void FlushLine(StringBuilder line, StringBuilder ascii)
    {
        while (line.Length < 60)
            line.Append(' ');
        line.Append('|').Append(ascii).Append('|');
        _writer.WriteLine(line.ToString());
        line.Clear();
        ascii.Clear();
    }

    private static string Timestamp()
    {
        return DateTimeOffset.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _writer.FlushAsync().ConfigureAwait(false);
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}