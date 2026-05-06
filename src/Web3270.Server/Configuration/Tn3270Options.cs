namespace Web3270.Server.Configuration;

/// <summary>
/// Strongly-typed binding of the <c>Tn3270</c> appsettings section.
/// Drives runtime behaviour that should be flippable per-environment
/// without recompiling.
/// </summary>
public sealed class Tn3270Options
{
    public const string SectionName = "Tn3270";

    public StreamCaptureOptions StreamCapture { get; set; } = new();
}

/// <summary>
/// Per-session hex/ASCII trace file (the <c>traces/&lt;id&gt;.log</c>
/// recorder). Disabled by default in production — opt in from
/// <c>appsettings.Development.json</c> or via env var
/// <c>Tn3270__StreamCapture__Enabled=true</c> when you need to capture
/// a session for offline debugging.
/// </summary>
public sealed class StreamCaptureOptions
{
    /// <summary>When false the recorder is null and no file is created.</summary>
    public bool Enabled { get; set; }

    /// <summary>Folder where trace files are written. Resolved relative to
    /// the host's content-root path when not absolute.</summary>
    public string Directory { get; set; } = "traces";
}
