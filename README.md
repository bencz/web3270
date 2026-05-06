# web3270

Browser-based TN3270 / TN3270E terminal that talks to real mainframes. The
data stream is parsed in C# (.NET 10), pushed to the browser through
SignalR as a structured snapshot, and rendered on an HTML5 canvas.

```
[browser canvas] <—SignalR (JSON)—> [ASP.NET Core hub] <—TCP/Telnet—> [mainframe]
```

# Screenshots

<img width="1509" height="905" alt="image" src="https://github.com/user-attachments/assets/408dbd04-c461-446a-b02b-7164d34eb7a6" />
<br/>
<img width="1509" height="909" alt="image" src="https://github.com/user-attachments/assets/5bf72ad5-7b69-48f8-8061-d7e69cad4107" />
<br/>
<img width="1512" height="910" alt="image" src="https://github.com/user-attachments/assets/92ed788e-2ea3-4225-b696-dba0c3476549" />
<br/>
<img width="1509" height="908" alt="image" src="https://github.com/user-attachments/assets/f8d0995f-63bf-468b-9a4d-24ea2df341d0" />



## What's implemented

### Telnet / TN3270E layer (RFC 854 / 1205 / 1041 / 2355)

- IAC negotiation: BINARY, EOR, TERMINAL-TYPE, SUPPRESS-GO-AHEAD, 3270-Regime.
- Full TN3270E sub-negotiation: DEVICE_TYPE REQUEST/IS, FUNCTIONS REQUEST/IS,
  CONNECT-LU support, REJECT handling.
- 5-byte TN3270E command header on every record once the session is in
  TN3270E mode (data type, request flag, response flag, sequence).
- Automatic POSITIVE_RESPONSE (DEVICE_END) when the host requests
  ALWAYS_RESPONSE — VTAM expects this acknowledgement.
- IAC 0xFF doubling on outbound, IAC EOR record framing on both sides.

### 3270 data stream parser

- Commands: W, EW, EWA, EAU, RB, RM, RMA, WSF.
- Orders: SF, SFE, SBA, SA, MF, IC, PT, RA, EUA, GE.
- Format Control Orders rendered as spaces: NUL, FF, CR, NL, EM, DUP, FM,
  SUB, 8-ones (matches dm3270's `FormatControlOrder.process`).
- 12-bit and 14-bit buffer addressing.
- WCC bit decoding: ResetMDT, KeyboardRestore, SoundAlarm, StartPrinter,
  PrinterFormat.
- SF / SFE attribute byte: protected, numeric, intensity, MDT, hidden.
- Per-character extended attributes via SA (foreground / background /
  highlight) — running state across the write so a single field can carry
  multi-coloured text (TK5 banner style).
- RA edge case: when current address equals the target, fills the entire
  buffer instead of being a no-op.
- EBCDIC CP037 codepage with bidirectional Unicode translation.

### Structured Fields

- Inbound dispatch: ReadPartition (Query / QueryList), EraseReset,
  SetReplyMode, Outbound 3270DS (recursive embedded write).
- Query Reply response generated automatically and sent back on Read
  Partition Query: Summary, UsableArea, CharacterSets, Color (16 pairs),
  Highlight (Default / Blink / Reverse / Underscore / Intensify),
  ReplyModes (Field / Extended Field / Character), ImplicitPartition.
  This is what allows TSO, CICS, IMS etc. to hand-shake capabilities and
  proceed past BIND.

### Outbound (terminal → host)

- Read-Modified packing with run-based emission: only contiguous cells the
  user actually typed (`cell.Modified`) are sent, with one SBA per run.
  Avoids echoing back host pre-fill spaces — without this real hosts (TSO
  on TK5/TK4-) reject every Enter with `IKT00405I` screen-erasure recovery.
- Cursor address resolved to the first user-modified cell, mirroring x3270
  / dm3270 / Soldier-of-Fortran tn3270lib behaviour.
- AID short-reads (PA1-PA3, Clear, SysReq) emit just the AID byte.

### Screen buffer

- Linear cell store with row-major addressing.
- Cyclic field discovery — the field that contains position 0 is the one
  whose attribute byte is the last one in the buffer; the snapshot factory
  resolves this wrap-around so cells before the first SF render correctly.
- 3279 base palette (protected/unprotected × normal/high) used when no
  extended colour was negotiated.
- Buffer reset to 0x00 on Erase Write (NULL, not space) so Read-Modified
  can suppress them per RFC 1205.

### Frontend (canvas + SignalR)

- Click-and-drag selection with translucent overlay.
- ⌘C / Ctrl+C copies the selection to the clipboard. Hidden cells (password
  fields) are emitted as spaces — credentials are never leaked.
- ⌘V / Ctrl+V pastes the clipboard into the cursor field as `Type` events
  (newlines/tabs collapsed to spaces).
- ⌘A / Ctrl+A selects the whole screen.
- Plain click moves the terminal cursor (without dragging).
- Cursor position display in the toolbar (1-indexed row / col).
- Rule toggle — thin cross-hair lines through the cursor row and column,
  x3270-style.
- Edge-triggered alarm: WCC SoundAlarm beeps once per pulse (server
  consumes the flag after every snapshot, client beeps only on
  false → true transitions).
- Single shared `AudioContext` for beeps to avoid resource exhaustion.
- AID buttons in the toolbar: Enter, Clear, PA1-PA3, PF1-PF12.
- Keyboard mappings:                                                                                  
  - **F1-F12** → PF1-PF12, **Shift+F1-F12** → PF13-PF24                                               
  - **Esc** → Clear                                                                                   
  - **PageUp / PageDown** → PF7 / PF8 (ISPF scroll convention)                                        
  - **Tab** → next unprotected field                                                                  
  - **Home** → first unprotected field                                                                
  - **End** → last typed position in the current field                                                
  - **Backspace** → erase previous char (NUL fill, MDT set)                                           
  - **Delete** → delete char at cursor and pull the rest of the field left                            
  - **Arrow keys** → move the cursor cell-by-cell (wraps at edges)  
- Per-session stream recorder writes a hex+ASCII dump of every TN3270
  record (in/out) to `traces/<timestamp>_<connectionId>.log` when enabled.
  Useful for offline parser debugging.

## Building and running

```bash
dotnet build
dotnet run --project src/Web3270.Server
```

Open `http://localhost:5001`, choose the terminal model, set host/port,
click **Connect**.

## Configuration (`appsettings.json`)

Logging is wired through Serilog and read from configuration:

```jsonc
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Web3270.Tn3270": "Information",
      "Web3270.Server":  "Information"
    }
  }
}
```

Lower a category to `Debug` or `Trace` to surface hex dumps of every
record. The `[LoggerMessage]` source-generated methods automatically gate
on `IsEnabled`, so the cost is zero when a level is disabled.

Per-session stream capture is opt-in:

```jsonc
"Tn3270": {
  "StreamCapture": {
    "Enabled": false,        // true = write traces/<id>.log per session
    "Directory": "traces"    // relative to ContentRoot, or absolute
  }
}
```

It is off by default in production. `appsettings.Development.json`
enables it. Override at runtime with the standard ASP.NET Core env var:
`Tn3270__StreamCapture__Enabled=true`.

## Public TN3270 hosts for testing

| Host                          | Port | Notes                |
| ----------------------------- | ---- | -------------------- |
| `mainframe.hercules-390.org`  | 3270 | Hercules + MVS 3.8j  |
| `tn3270.themainframe.org`     | 23   | z/OS                 |
| `pub400.com`                  | 23   | It works, but with some caveats!!! |

## Wire format (browser ↔ server)

### `ScreenUpdate` (server → client)

```jsonc
{
  "rows": 24,
  "columns": 80,
  "cursor": 12,
  "keyboardLocked": false,
  "alarm": false,
  "cells": [
    { "glyph": "H", "protected": false, "hidden": false, "highlight": false,
      "modified": false, "foreground": 240, "background": 0, "highlightAttr": 244 }
    /* ... rows*columns entries, row-major ... */
  ],
  "fields": [
    { "start": 1, "length": 40, "protected": true, "numeric": false, "hidden": false }
  ]
}
```

### `SendKey` (client → server)

```jsonc
{ "kind": "Aid",       "value": "Enter|PF1..PF24|PA1..PA3|Clear|SysReq" }
{ "kind": "Type",      "value": "single character or string" }
{ "kind": "Cursor",    "address": 123 }
{ "kind": "Tab" }
{ "kind": "Backspace" }
{ "kind": "Delete" }
```

## Trace files

When `Tn3270:StreamCapture:Enabled=true`, two endpoints are exposed for
grabbing per-session captures:

- `GET /traces` — list captured sessions (newest first).
- `GET /traces/<filename>` — download a specific log.

Each line in a capture is timestamped and prefixed with direction
(`<<` inbound, `>>` outbound) plus a hexdump-style payload, matching what
the parser saw on the wire after IAC framing was stripped.

## Known limitations

- IND$FILE file transfer is not implemented (the Structured Field
  identifier 0xD0 is recognised but no file transfer state machine).
- Only EBCDIC code page CP037 (US English / Latin-1). Other code pages
  require additional translation tables.
- Field validation, field outlining and programmed symbols (loadable
  character sets) are parsed but not enforced.
- Insert mode (ESC + Insert) is not implemented — typing always overwrites.

## References

- RFC 854 — Telnet protocol.
- RFC 1205 — TN3270 (plain).
- RFC 2355 — TN3270E.
- IBM 3270 Information Display System Data Stream Programmer's Reference.
