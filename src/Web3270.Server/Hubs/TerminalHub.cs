using Microsoft.AspNetCore.SignalR;
using Web3270.Server.Models;
using Web3270.Server.Services;
using Web3270.Tn3270;
using Web3270.Tn3270.DataStream;

namespace Web3270.Server.Hubs;

/// <summary>
/// SignalR hub exposing the TN3270 session to the browser. The hub itself
/// is per-call; the actual session lives in <see cref="ITerminalSessionManager"/>.
/// </summary>
public sealed class TerminalHub : Hub
{
    public const string Path = "/hubs/terminal";

    private readonly ITerminalSessionManager _manager;
    private readonly IHubContext<TerminalHub> _hubContext;
    private readonly ILogger<TerminalHub> _log;

    public TerminalHub(
        ITerminalSessionManager manager,
        IHubContext<TerminalHub> hubContext,
        ILogger<TerminalHub> log)
    {
        _manager = manager;
        _hubContext = hubContext;
        _log = log;
    }

    public async Task Connect(ConnectRequest request)
    {
        var connectionId = Context.ConnectionId;

        try
        {
            await _manager.StartAsync(connectionId, request, OnUpdate, OnDisconnect, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("Connected");
        }
        catch (Exception ex)
        {
            _log.ConnectFailed(connectionId, ex);
            await Clients.Caller.SendAsync("Error", ex.Message);
        }

        return;

        Task OnUpdate(Tn3270Session session)
        {
            var snapshot = ScreenSnapshotFactory.Capture(session.Buffer);
            return _hubContext.Clients.Client(connectionId).SendAsync("ScreenUpdate", snapshot);
        }

        Task OnDisconnect(Tn3270Session _, Exception ex)
        {
            return _hubContext.Clients.Client(connectionId).SendAsync("Disconnected", ex?.Message ?? "host closed");
        }
    }

    public Task Disconnect()
    {
        return _manager.StopAsync(Context.ConnectionId);
    }

    public async Task SendKey(KeyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var session = _manager.Find(Context.ConnectionId);
        if (session is null)
        {
            await Clients.Caller.SendAsync("Error", "no active session");
            return;
        }

        _log.HubKeyReceived(Context.ConnectionId, input.Kind, input.Value ?? string.Empty, input.Address);
        _log.HubBufferState(Context.ConnectionId, session.Buffer.CursorAddress, session.Buffer.KeyboardLocked);

        switch (input.Kind)
        {
            case "Type":
                if (!string.IsNullOrEmpty(input.Value))
                {
                    foreach (var ch in input.Value) session.TypeCharacter(ch);
                    await PushSnapshot(session);
                }

                break;
            case "Backspace":
                session.Backspace();
                await PushSnapshot(session);
                break;
            case "Tab":
                session.TabToNextField();
                await PushSnapshot(session);
                break;
            case "Cursor":
                if (input.Address.HasValue)
                    session.MoveCursor(input.Address.Value);
                await PushSnapshot(session);
                break;
            default:
                var aid = AidKey.ForName(input.Value ?? input.Kind);
                await session.SendAidAsync(aid, Context.ConnectionAborted);
                break;
        }
    }

    private Task PushSnapshot(Tn3270Session session)
    {
        var snapshot = ScreenSnapshotFactory.Capture(session.Buffer);
        return Clients.Caller.SendAsync("ScreenUpdate", snapshot);
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        await _manager.StopAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
