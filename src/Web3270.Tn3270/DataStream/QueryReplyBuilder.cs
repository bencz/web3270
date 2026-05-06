namespace Web3270.Tn3270.DataStream;

/// <summary>
/// Builds the full inbound Query Reply record sent in response to a host
/// "Read Partition Query" structured field. The shape matches the
/// dm3270 reference (ReadStructuredFieldCommand.buildReply) which is what
/// real-world hosts (TSO, CICS, IMS, MVS) expect to see during BIND so
/// they know what features they can safely use on this terminal.
///
/// Wire format:
///   0x88                         AID structured field
///   { 2 bytes length | 0x81 QUERY_REPLY | type | payload }*
///
/// We emit Summary, UsableArea, CharacterSets, Color, Highlight,
/// ReplyModes and ImplicitPartition. That set is the absolute minimum
/// every modern host expects — without it many flavours of MVS will
/// refuse to drive the screen and throw IKT00405I screen-erasure
/// recovery on the first AID press.
/// </summary>
public sealed class QueryReplyBuilder
{
    private readonly int _rows;
    private readonly int _cols;

    public QueryReplyBuilder(int rows, int columns)
    {
        if (rows <= 0 || columns <= 0)
            throw new ArgumentException("rows/columns must be positive");
        _rows = rows;
        _cols = columns;
    }

    public byte[] Build()
    {
        var fields = new List<byte[]>(8)
        {
            BuildUsableArea(),
            BuildCharacterSets(),
            BuildColor(),
            BuildHighlight(),
            BuildReplyModes(),
            BuildImplicitPartition()
        };

        // Summary lists the type codes of all of the above + itself.
        fields.Insert(0, BuildSummary(fields));

        var totalSize = 1 + fields.Sum(f => f.Length); // AID byte
        var output = new byte[totalSize];
        output[0] = AidKey.StructuredField;
        var ptr = 1;
        foreach (var f in fields)
        {
            Array.Copy(f, 0, output, ptr, f.Length);
            ptr += f.Length;
        }

        return output;
    }

    private static byte[] BuildSummary(List<byte[]> existing)
    {
        // existing fields' type bytes are at offset 3 of each.
        // Summary lists Summary + every other type.
        var typeCount = existing.Count + 1;
        var size = 4 + typeCount; // 2 length + 0x81 + type + payload
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.Summary);
        var ptr = 4;
        bytes[ptr++] = QueryReplyType.Summary;
        foreach (var f in existing) bytes[ptr++] = f[3]; // type byte position
        return bytes;
    }

    private byte[] BuildUsableArea()
    {
        // Layout follows dm3270.UsableArea (model 2 defaults, dimensions
        // patched in for the configured screen size).
        var rest = new byte[]
        {
            0x01, 0x00, // flags1+flags2 (12/14-bit addressing)
            0x00, 0x00, 0x00, 0x00, // width + height placeholders
            0x01, 0x00, // units (inches) + spare
            0xD3, 0x03, // X numerator (979)
            0x20, 0x00, // X denominator (32)
            0x9E, 0x02, // Y numerator (670)
            0x58, 0x07, // Y denominator (1880)
            0x0C, 0x07, // X units (12) Y units (7)
            0x80 // 4-byte spare for buffer size
        };
        var size = 4 + rest.Length + 2; // +2 = buffer size (1920)
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.UsableArea);
        Array.Copy(rest, 0, bytes, 4, rest.Length);
        // Width / height (columns / rows)
        WriteUInt16BE(bytes, 4 + 2, (ushort)_cols);
        WriteUInt16BE(bytes, 4 + 4, (ushort)_rows);
        // Buffer size
        WriteUInt16BE(bytes, 4 + rest.Length, 1920);
        return bytes;
    }

    private static byte[] BuildCharacterSets()
    {
        // Single character set (CP037 — alphanumeric + symbols).
        // Same opaque payload dm3270 uses; tested to satisfy MVS query.
        var rest = new byte[]
        {
            0x82, 0x00, 0x07, 0x0C, 0x00, 0x00, 0x00, 0x00,
            0x07, 0x00, 0x00, 0x00, 0x02, 0xB9, 0x04, 0x17,
            0x01, 0x00, 0xF1, 0x03, 0xC3, 0x01, 0x36
        };
        var size = 4 + rest.Length;
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.CharacterSets);
        Array.Copy(rest, 0, bytes, 4, rest.Length);
        return bytes;
    }

    private static byte[] BuildColor()
    {
        // 16 colour pairs (default + named colours) — must match the
        // extended foreground/background bytes 0xF1-0xF7 + 0xF8-0xFF.
        var size = 4 + 2 + 16 * 2;
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.Color);
        var ptr = 4;
        bytes[ptr++] = 0x00; // flags
        bytes[ptr++] = 0x10; // 16 pairs

        var pairs = new (byte attr, byte action)[]
        {
            (0xF0, 0xF4), // neutral default → green
            (0xF1, 0xF1), // blue
            (0xF2, 0xF2), // red
            (0xF3, 0xF3), // pink
            (0xF4, 0xF4), // green
            (0xF5, 0xF5), // turquoise
            (0xF6, 0xF6), // yellow
            (0xF7, 0xF7), // neutral2 (white)
            (0xF8, 0xF8), // black
            (0xF9, 0xF9), // deep blue
            (0xFA, 0xFA), // orange
            (0xFB, 0xFB), // purple
            (0xFC, 0xFC), // pale green
            (0xFD, 0xFD), // pale turquoise
            (0xFE, 0xFE), // grey
            (0xFF, 0xFF) // white
        };
        foreach (var (attr, action) in pairs)
        {
            bytes[ptr++] = attr;
            bytes[ptr++] = action;
        }

        return bytes;
    }

    private static byte[] BuildHighlight()
    {
        // 5 (attr → action) pairs covering blink / reverse / underscore / intensify.
        var size = 4 + 1 + 5 * 2;
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.Highlight);
        var ptr = 4;
        bytes[ptr++] = 0x05; // pairs

        bytes[ptr++] = Highlighting.Default;
        bytes[ptr++] = 0xF0; // default → normal
        bytes[ptr++] = Highlighting.Blink;
        bytes[ptr++] = Highlighting.Blink;
        bytes[ptr++] = Highlighting.Reverse;
        bytes[ptr++] = Highlighting.Reverse;
        bytes[ptr++] = Highlighting.Underline;
        bytes[ptr++] = Highlighting.Underline;
        bytes[ptr++] = Highlighting.Intensify;
        bytes[ptr++] = Highlighting.Intensify;
        return bytes;
    }

    private static byte[] BuildReplyModes()
    {
        // We accept Field, Extended field and Character reply modes.
        var size = 4 + 3;
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.ReplyModes);
        bytes[4] = 0x00; // field mode
        bytes[5] = 0x01; // extended field mode
        bytes[6] = 0x02; // character mode
        return bytes;
    }

    private byte[] BuildImplicitPartition()
    {
        var size = 4 + 13;
        var bytes = new byte[size];
        WriteHeader(bytes, size, QueryReplyType.ImplicitPartition);
        bytes[4] = 0x00; // flags
        bytes[5] = 0x00;
        bytes[6] = 0x0B; // length
        bytes[7] = 0x01; // partition implicit
        bytes[8] = 0x00;

        // default screen (model 2: 80x24)
        WriteUInt16BE(bytes, 9, 80);
        WriteUInt16BE(bytes, 11, 24);
        // alternate screen — the configured size
        WriteUInt16BE(bytes, 13, (ushort)_cols);
        WriteUInt16BE(bytes, 15, (ushort)_rows);
        return bytes;
    }

    private static void WriteHeader(byte[] dest, int size, byte type)
    {
        WriteUInt16BE(dest, 0, (ushort)size);
        dest[2] = StructuredFieldType.QueryReply;
        dest[3] = type;
    }

    private static void WriteUInt16BE(byte[] dest, int offset, ushort value)
    {
        dest[offset] = (byte)((value >> 8) & 0xFF);
        dest[offset + 1] = (byte)(value & 0xFF);
    }
}