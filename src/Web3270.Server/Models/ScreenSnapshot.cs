namespace Web3270.Server.Models;

/// <summary>
/// DTO sent to the browser whenever the host updates the buffer. The wire
/// format is JSON; cells are serialised as a flat array (row-major) to keep
/// the message compact.
/// </summary>
public sealed class ScreenSnapshot
{
    public int Rows { get; set; }
    public int Columns { get; set; }
    public int Cursor { get; set; }
    public bool KeyboardLocked { get; set; }
    public bool Alarm { get; set; }
    public CellSnapshot[] Cells { get; set; } = [];
    public FieldSnapshot[] Fields { get; set; } = [];
}

public sealed class CellSnapshot
{
    public string Glyph { get; set; } = " ";
    public bool Protected { get; set; }
    public bool Hidden { get; set; }
    public bool Highlight { get; set; }
    public bool Modified { get; set; }
    public byte Foreground { get; set; }
    public byte Background { get; set; }
    public byte HighlightAttr { get; set; }
}

public sealed class FieldSnapshot
{
    public int Start { get; set; }
    public int Length { get; set; }
    public bool Protected { get; set; }
    public bool Numeric { get; set; }
    public bool Hidden { get; set; }
}

public sealed class ConnectRequest
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 23;
    public string TerminalType { get; set; } = "IBM-3278-2-E";
    public int Rows { get; set; } = 24;
    public int Columns { get; set; } = 80;
}

public sealed class KeyInput
{
    /// <summary>
    /// Either an AID name ("Enter", "PF1", "Clear", ...), a single character
    /// ("Type"), or a navigation key ("Tab", "Backspace", "Cursor").
    /// </summary>
    public string Kind { get; set; } = "Enter";

    public string Value { get; set; }
    public int? Address { get; set; }
}