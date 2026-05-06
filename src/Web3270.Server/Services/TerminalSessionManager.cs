using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using Web3270.Server.Configuration;
using Web3270.Server.Models;
using Web3270.Tn3270;
using Web3270.Tn3270.Telnet;

namespace Web3270.Server.Services;

public sealed class TerminalSessionManager : ITerminalSessionManager, IAsyncDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TerminalSessionManager> _log;
    private readonly IHostEnvironment _env;
    private readonly Tn3270Options _options;
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    public TerminalSessionManager(
        ILoggerFactory loggerFactory,
        ILogger<TerminalSessionManager> log,
        IHostEnvironment env,
        IOptions<Tn3270Options> options)
    {
        _loggerFactory = loggerFactory;
        _log = log;
        _env = env;
        _options = options.Value;
    }

    public async Task<Tn3270Session> StartAsync(
        string connectionId,
        ConnectRequest request,
        Func<Tn3270Session, Task> onUpdate,
        Func<Tn3270Session, Exception, Task> onDisconnect,
        CancellationToken ct)
    {
        await StopAsync(connectionId).ConfigureAwait(false);

        // Stream capture is gated on the appsettings switch
        // (Tn3270:StreamCapture:Enabled). When disabled the recorder is
        // null and no file is created — a genuine no-op on disk.
        IStreamRecorder recorder = null;
        string tracePath = null;
        if (_options.StreamCapture.Enabled)
        {
            tracePath = ResolveTracePath(connectionId);
            recorder = new FileStreamRecorder(tracePath);
        }

        var options = new Tn3270SessionOptions
        {
            Host = request.Host,
            Port = request.Port,
            TerminalType = request.TerminalType,
            Rows = request.Rows,
            Columns = request.Columns,
            TraceFile = tracePath
        };

        var session = new Tn3270Session(options, _loggerFactory.CreateLogger<Tn3270Session>(), recorder);
        session.ScreenUpdated += onUpdate;
        session.Disconnected += onDisconnect;

        var entry = new SessionEntry(session, recorder, tracePath);
        _sessions[connectionId] = entry;

        try
        {
            await session.ConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _sessions.TryRemove(connectionId, out _);
            await session.DisposeAsync().ConfigureAwait(false);
            if (recorder is not null)
                await recorder.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _log.SessionStarted(connectionId);
        if (tracePath is not null)
            _log.SessionTraceFile(tracePath);
        return session;
    }

    public Tn3270Session Find(string connectionId)
    {
        return _sessions.TryGetValue(connectionId, out var entry) ? entry.Session : null;
    }

    public string FindTraceFile(string connectionId)
    {
        return _sessions.TryGetValue(connectionId, out var entry) ? entry.TracePath : null;
    }

    public async Task StopAsync(string connectionId)
    {
        if (_sessions.TryRemove(connectionId, out var entry))
        {
            await entry.Session.DisposeAsync().ConfigureAwait(false);
            if (entry.Recorder is not null)
                await entry.Recorder.DisposeAsync().ConfigureAwait(false);
            _log.SessionStopped(connectionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, entry) in _sessions)
        {
            await entry.Session.DisposeAsync().ConfigureAwait(false);
            if (entry.Recorder is not null)
                await entry.Recorder.DisposeAsync().ConfigureAwait(false);
        }

        _sessions.Clear();
    }

    private string ResolveTracePath(string connectionId)
    {
        var dir = _options.StreamCapture.Directory ?? "traces";
        if (!Path.IsPathRooted(dir))
            dir = Path.Combine(_env.ContentRootPath, dir);
        Directory.CreateDirectory(dir);
        var safeId = SanitizeId(connectionId);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return Path.Combine(dir, $"{stamp}_{safeId}.log");
    }

    private static string SanitizeId(string id)
    {
        var chars = new char[id.Length];
        for (var i = 0; i < id.Length; i++)
        {
            var c = id[i];
            chars[i] = char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_';
        }

        return new string(chars);
    }

    private sealed record SessionEntry(Tn3270Session Session, IStreamRecorder Recorder, string TracePath);
}
