using Web3270.Tn3270.Screen;

namespace Web3270.Tn3270.DataStream;

/// <summary>
/// Constructs an outbound 3270 record: the data the terminal sends back
/// to the host when the user presses an AID key.
///
/// Long form (Enter, PFx) layout:
///   AID | cursor-hi | cursor-lo |
///       { SBA field-start-hi field-start-lo &lt;non-null field bytes&gt; }*
///
/// Short form (PA1-PA3, Clear, SysReq):
///   AID
///
/// Default Read Modified emits each MDT-marked field from its first
/// content position and suppresses only NULL bytes. Host-filled blanks
/// (0x40) are non-NULL and must be returned with the field; this matches
/// the dm3270 ScreenPacker behavior and the 3270 Read Modified data stream.
/// Read Modified All uses the same field selection; it only disables the
/// PA/Clear short-read special case.
/// Unformatted screens have no fields, so they fall back to runs of
/// user-modified cells.
/// </summary>
public sealed class Tn3270OutboundBuilder
{
    private readonly ScreenBuffer _buffer;

    public Tn3270OutboundBuilder(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    public byte[] BuildReadModified(byte aid, bool readModifiedAll = false)
    {
        if (!readModifiedAll && AidKey.IsShortRead(aid))
            return [aid];

        var output = new List<byte>(64) { aid };
        var (cHi, cLo) = BufferAddress.Encode12(_buffer.CursorAddress);
        output.Add(cHi);
        output.Add(cLo);

        var fields = _buffer.Fields();
        foreach (var field in fields)
        {
            if (field.Protected) 
                continue;
            if (!field.Modified)
                continue;
            EmitModifiedField(output, field);
        }

        // Unformatted screens (no SF ever issued) have no fields. In that
        // case fall back to emitting any user-modified cells anywhere in
        // the buffer.
        if (fields.Count == 0) 
            EmitUnformattedRuns(output, readModifiedAll);

        return output.ToArray();
    }

    private void EmitModifiedField(List<byte> output, ScreenField field)
    {
        var (hi, lo) = BufferAddress.Encode12(field.Start);
        output.Add(Tn3270Order.SBA);
        output.Add(hi);
        output.Add(lo);

        var p = field.Start;
        for (var n = 0; n < field.Length; n++)
        {
            var cell = _buffer[p];
            if (!cell.IsFieldAttribute && cell.Character != 0x00)
                output.Add(cell.Character);
            p = (p + 1) % _buffer.Size;
        }
    }

    private void EmitUnformattedRuns(List<byte> output, bool readModifiedAll)
    {
        for (var i = 0; i < _buffer.Size; i++)
        {
            if (!ShouldEmit(_buffer[i], readModifiedAll)) 
                continue;
            var (hi, lo) = BufferAddress.Encode12(i);
            output.Add(Tn3270Order.SBA);
            output.Add(hi);
            output.Add(lo);
            while (i < _buffer.Size && ShouldEmit(_buffer[i], readModifiedAll))
            {
                var b = _buffer[i].Character;
                if (b == 0x00)
                    b = 0x40;
                output.Add(b);
                i++;
            }

            i--; // outer loop will increment again
        }
    }

    private static bool ShouldEmit(ScreenCell cell, bool readModifiedAll)
    {
        if (cell.IsFieldAttribute) 
            return false;
        if (readModifiedAll) 
            return cell.Character != 0x00;
        return cell.Modified;
    }

    public byte[] BuildReadBuffer(byte aid)
    {
        var output = new List<byte>(_buffer.Size + 32) { aid };

        var (cHi, cLo) = BufferAddress.Encode12(_buffer.CursorAddress);
        output.Add(cHi);
        output.Add(cLo);

        for (var i = 0; i < _buffer.Size; i++)
        {
            var cell = _buffer[i];
            if (cell.IsFieldAttribute)
            {
                output.Add(Tn3270Order.SF);
                output.Add(cell.Attribute.Raw);
            }
            else
            {
                output.Add(cell.Character);
            }
        }

        return output.ToArray();
    }
}