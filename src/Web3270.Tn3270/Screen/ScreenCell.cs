using Web3270.Tn3270.DataStream;

namespace Web3270.Tn3270.Screen;

/// <summary>
/// One position in the 3270 buffer. Carries the EBCDIC byte plus the
/// extended attributes that apply at that position. Field-attribute
/// positions (those owned by an SF/SFE order) carry IsFieldAttribute=true
/// and their <see cref="Character"/> renders as a blank.
/// </summary>
public sealed class ScreenCell
{
    public byte Character { get; set; }
    public bool IsFieldAttribute { get; set; }
    public FieldAttr Attribute { get; set; } = FieldAttr.Default;
    public byte Foreground { get; set; }
    public byte Background { get; set; }
    public byte Highlight { get; set; }
    public bool Modified { get; set; }

    public char ToUnicode()
    {
        return IsFieldAttribute
            ? ' '
            : Encoding.Ebcdic.ToUnicode(Character);
    }

    public void Reset()
    {
        // 3270 architecture: after Erase Write the entire buffer is set to
        // NULLS (0x00), not spaces. Read Modified suppresses these NULLs;
        // host-written blanks remain real field content.
        Character = 0x00;
        IsFieldAttribute = false;
        Attribute = FieldAttr.Default;
        Foreground = 0;
        Background = 0;
        Highlight = 0;
        Modified = false;
    }

    public ScreenCell Clone()
    {
        return new ScreenCell
        {
            Character = Character,
            IsFieldAttribute = IsFieldAttribute,
            Attribute = Attribute,
            Foreground = Foreground,
            Background = Background,
            Highlight = Highlight,
            Modified = Modified
        };
    }
}