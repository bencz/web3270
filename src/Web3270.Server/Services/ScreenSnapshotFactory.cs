using Web3270.Server.Models;
using Web3270.Tn3270.DataStream;
using Web3270.Tn3270.Encoding;
using Web3270.Tn3270.Screen;

namespace Web3270.Server.Services;

/// <summary>
/// Snapshots a <see cref="ScreenBuffer"/> into a wire-friendly DTO with
/// extended attributes propagated to every cell of each field.
/// 3270 buffers are cyclic: the field that owns position 0 is the field
/// whose attribute byte is the *last* one in the buffer. This factory
/// pre-resolves that wrap-around field so cells before the first SF
/// inherit the right attributes.
/// </summary>
public static class ScreenSnapshotFactory
{
    public static ScreenSnapshot Capture(ScreenBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var size = buffer.Size;
        var cells = new CellSnapshot[size];
        for (var i = 0; i < size; i++)
            cells[i] = new CellSnapshot();

        var fields = buffer.Fields();
        var fieldDtos = new FieldSnapshot[fields.Count];

        // Resolve the wrap-around field: the LAST attribute byte position.
        // Every cell before the first attribute byte inherits this field's
        // attributes.
        var currentAttr = FieldAttr.Default;
        byte fg = 0, bg = 0, hi = 0;
        var inField = false;

        if (fields.Count > 0)
        {
            ScreenCell wrap = null;
            for (var i = size - 1; i >= 0; i--)
            {
                if (!buffer[i].IsFieldAttribute)
                    continue;
                wrap = buffer[i];
                break;
            }

            if (wrap is not null)
            {
                currentAttr = wrap.Attribute;
                fg = wrap.Foreground;
                bg = wrap.Background;
                hi = wrap.Highlight;
                inField = true;
            }
        }

        for (var i = 0; i < size; i++)
        {
            var src = buffer[i];
            if (src.IsFieldAttribute)
            {
                currentAttr = src.Attribute;
                fg = src.Foreground;
                bg = src.Background;
                hi = src.Highlight;
                inField = true;

                cells[i].Glyph = " ";
                cells[i].Protected = true;
                cells[i].Hidden = true;
                cells[i].Highlight = false;
                cells[i].Modified = currentAttr.Modified;
                cells[i].Foreground = fg;
                cells[i].Background = bg;
                cells[i].HighlightAttr = hi;
                continue;
            }

            var ch = inField && currentAttr.Hidden ? ' ' : Ebcdic.ToUnicode(src.Character);
            cells[i].Glyph = ch.ToString();
            cells[i].Protected = inField && currentAttr.Protected;
            cells[i].Hidden = inField && currentAttr.Hidden;
            cells[i].Highlight = inField && currentAttr.Highlighted;
            cells[i].Modified = inField && currentAttr.Modified;
            // Per-cell extended attributes (set by SA / SFE) override the
            // field-level fall-through. A zero byte means "use field default".
            cells[i].Foreground = src.Foreground != 0 ? src.Foreground : fg;
            cells[i].Background = src.Background != 0 ? src.Background : bg;
            cells[i].HighlightAttr = src.Highlight != 0 ? src.Highlight : hi;
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            fieldDtos[i] = new FieldSnapshot
            {
                Start = f.Start,
                Length = f.Length,
                Protected = f.Protected,
                Numeric = f.Numeric,
                Hidden = f.Hidden
            };
        }

        // AlarmTriggered is edge-triggered: the host's WCC SoundAlarm bit
        // is a single beep instruction, not a sustained state. We snapshot
        // it once and clear the flag so subsequent snapshots — typically
        // pushed after every keystroke — don't replay the beep ad infinitum.
        var alarm = buffer.AlarmTriggered;
        buffer.AlarmTriggered = false;

        return new ScreenSnapshot
        {
            Rows = buffer.Rows,
            Columns = buffer.Columns,
            Cursor = buffer.CursorAddress,
            KeyboardLocked = buffer.KeyboardLocked,
            Alarm = alarm,
            Cells = cells,
            Fields = fieldDtos
        };
    }
}