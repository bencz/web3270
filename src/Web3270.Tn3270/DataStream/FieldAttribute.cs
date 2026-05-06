namespace Web3270.Tn3270.DataStream;

/// <summary>
/// Decoded view over a 3270 field attribute byte (the basic 8-bit form).
/// Bits:
///   7 6 — must be valid 6-bit graphic (set by host)
///   5   — protected
///   4   — numeric (only digits + sign on input)
///   3 2 — display / intensity
///   1   — reserved
///   0   — modified data tag
/// </summary>
public readonly record struct FieldAttr(byte Raw)
{
    public bool Protected => (Raw & 0x20) != 0;
    public bool Numeric => (Raw & 0x10) != 0;
    public bool Modified => (Raw & 0x01) != 0;

    public DisplayIntensity Intensity =>
        (DisplayIntensity)((Raw & 0x0C) >> 2);

    public bool Hidden => Intensity == DisplayIntensity.NonDisplay;
    public bool Highlighted => Intensity == DisplayIntensity.High;

    public FieldAttr WithModified(bool modified) 
        => new((byte)(modified ? Raw | 0x01 : Raw & ~0x01));

    public static FieldAttr Default => new(0xC0);
}

public enum DisplayIntensity : byte
{
    Normal = 0,
    Selectable = 1,
    High = 2,
    NonDisplay = 3
}

public static class ExtendedAttributeType
{
    public const byte ResetAll = 0x00;
    public const byte FieldAttribute = 0xC0;
    public const byte FieldValidation = 0xC1;
    public const byte FieldOutlining = 0xC2;
    public const byte Highlighting = 0x41;
    public const byte ForegroundColor = 0x42;
    public const byte CharacterSet = 0x43;
    public const byte BackgroundColor = 0x45;
    public const byte Transparency = 0x46;
}

public static class Highlighting
{
    public const byte Default = 0x00;
    public const byte Blink = 0xF1;
    public const byte Reverse = 0xF2;
    public const byte Underline = 0xF4;
    public const byte Intensify = 0xF8;
}

public static class Color3270
{
    public const byte Default = 0x00;
    public const byte Neutral = 0xF0;
    public const byte Blue = 0xF1;
    public const byte Red = 0xF2;
    public const byte Pink = 0xF3;
    public const byte Green = 0xF4;
    public const byte Turquoise = 0xF5;
    public const byte Yellow = 0xF6;
    public const byte White = 0xF7;
    public const byte Black = 0xF8;
    public const byte DeepBlue = 0xF9;
    public const byte Orange = 0xFA;
    public const byte Purple = 0xFB;
    public const byte PaleGreen = 0xFC;
    public const byte PaleTurquoise = 0xFD;
    public const byte Grey = 0xFE;
    public const byte WhiteAlt = 0xFF;
}