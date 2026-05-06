using Web3270.Tn3270.DataStream;

namespace Web3270.Tn3270.Screen;

/// <summary>
/// Materialised view of a 3270 field, computed by walking the buffer.
/// </summary>
public sealed class ScreenField
{
    /// <summary>Position of the field-attribute byte (the SF/SFE position).</summary>
    public int AttributePosition { get; init; }

    /// <summary>First content position (immediately after the attribute).</summary>
    public int Start { get; init; }

    /// <summary>Length of the content (positions, excluding the attribute byte).</summary>
    public int Length { get; init; }

    /// <summary>End of the content area (inclusive, modular wrap).</summary>
    public int End { get; init; }

    public FieldAttr Attribute { get; init; }

    public bool Protected => Attribute.Protected;
    public bool Numeric => Attribute.Numeric;
    public bool Hidden => Attribute.Hidden;
    public bool Modified => Attribute.Modified;
}