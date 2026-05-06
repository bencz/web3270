using Web3270.Tn3270.DataStream;

namespace Web3270.Tn3270.Screen;

/// <summary>
/// The terminal display buffer. Cells are stored linearly (row-major) and
/// fields are derived on demand by scanning for field-attribute positions.
/// </summary>
public sealed class ScreenBuffer
{
    private readonly ScreenCell[] _cells;

    public ScreenBuffer(int rows, int columns)
    {
        if (rows <= 0 || columns <= 0)
            throw new ArgumentException("rows/columns must be positive");

        Rows = rows;
        Columns = columns;
        _cells = new ScreenCell[rows * columns];
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new ScreenCell();
            _cells[i].Reset();
        }
    }

    public int Rows { get; }
    public int Columns { get; }
    public int Size => Rows * Columns;
    public int CursorAddress { get; set; }
    public bool KeyboardLocked { get; set; } = true;
    public bool AlarmTriggered { get; set; }

    public ScreenCell this[int address] => _cells[Wrap(address)];

    public ScreenCell At(int row, int col)
    {
        return _cells[row * Columns + col];
    }

    public int Wrap(int address)
    {
        var size = Size;
        var a = address % size;
        return a < 0 ? a + size : a;
    }

    /// <summary>Erases the entire buffer and clears MDT bits.</summary>
    public void EraseAll()
    {
        for (var i = 0; i < _cells.Length; i++) 
            _cells[i].Reset();
        CursorAddress = 0;
    }

    /// <summary>Erases all unprotected positions (used by EAU and Clear).</summary>
    public void EraseAllUnprotected()
    {
        var current = FieldAttr.Default;
        var insideField = false;
        foreach (var t in _cells)
        {
            if (t.IsFieldAttribute)
            {
                current = t.Attribute;
                insideField = true;
                t.Attribute = current.WithModified(false);
                continue;
            }

            if (insideField && !current.Protected)
                t.Character = 0x00;
        }
    }

    /// <summary>
    /// Walks the buffer and produces the field list. Returns an empty list
    /// for unformatted buffers (no SF/SFE has ever been issued).
    /// </summary>
    public IReadOnlyList<ScreenField> Fields()
    {
        var list = new List<ScreenField>();
        var first = -1;
        for (var i = 0; i < _cells.Length; i++)
            if (_cells[i].IsFieldAttribute)
            {
                first = i;
                break;
            }

        if (first < 0)
            return list;

        var idx = first;
        do
        {
            var attr = _cells[idx].Attribute;
            var start = (idx + 1) % Size;
            var next = FindNextAttribute(start);
            int end, length;
            if (next < 0)
            {
                next = first;
                length = (next - start + Size) % Size;
                if (length == 0) length = Size;
                end = (start + length - 1) % Size;
            }
            else
            {
                length = (next - start + Size) % Size;
                end = (start + length - 1 + Size) % Size;
            }

            list.Add(new ScreenField
            {
                AttributePosition = idx,
                Start = start,
                Length = length,
                End = end,
                Attribute = attr
            });

            idx = next;
        } while (idx != first && idx >= 0);

        return list;
    }

    public ScreenField FieldAt(int address)
    {
        var fields = Fields();
        return fields.Count == 0 
            ? null 
            : fields.FirstOrDefault(f => Contains(f, address));
    }

    private static bool Contains(ScreenField field, int address)
    {
        if (field.Length == 0)
            return false;
        if (field.Start <= field.End)
            return address >= field.Start && address <= field.End;
        return address >= field.Start || address <= field.End;
    }

    private int FindNextAttribute(int from)
    {
        for (var n = 0; n < Size; n++)
        {
            var idx = (from + n) % Size;
            if (_cells[idx].IsFieldAttribute) 
                return idx;
        }

        return -1;
    }

    public IEnumerable<ScreenCell> EnumerateAll() 
        => _cells;
}