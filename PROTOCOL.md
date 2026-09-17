# PocketInk wire protocol (INPUT_PACKET_V1)

One WebSocket connection at `/ws` carries everything: a small JSON control channel (text frames)
and a binary pointer-input channel (binary frames). `CurrentProtocolVersion = 1`
([ProtocolConstants.cs](src/PocketInk.Core/Protocol/ProtocolConstants.cs)).

Both encoders are implemented twice, deliberately: once in C#
([InputPacket.cs](src/PocketInk.Core/Protocol/InputPacket.cs)) and once in JavaScript
([protocol.js](src/PocketInk.Host/wwwroot/js/protocol.js)). A golden-vector test
([InputPacketTests.cs](tests/PocketInk.Tests/InputPacketTests.cs) and
[protocol.golden.test.js](tests/js/protocol.golden.test.js)) checks byte-for-byte agreement
between them on every `scripts/test.ps1` run.

## Connection lifecycle

1. Phone GETs `/pair?token=...` (from the QR code) → the SPA fallback serves the client shell.
2. Client JS POSTs `/api/pair` with the token → server sets an `HttpOnly`, `SameSite=Strict`
   session cookie (`pocketink_session`). Plain HTTP by design (LAN-only) - `Secure=false`
   deliberately, since `Secure=true` would silently drop the cookie over plain HTTP rather than
   add any real protection.
3. Client opens `ws://<host>/ws`, which the server accepts only if: it's a genuine WebSocket
   upgrade, the `Origin` header (when present) matches the serving origin, and the session cookie
   is valid.
4. Client sends `hello` within 5 seconds or the server closes the connection.
5. Server replies `hello-ack` (or `error` + close, on a protocol version mismatch).
6. Either side may now exchange binary `InputPacket` frames and the JSON control messages below.

## Binary frame: INPUT_PACKET_V1

Fixed 28 bytes, all multi-byte fields **little-endian**:

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 2 | Magic | `0x504B` ("PK") |
| 2 | 1 | Version | `1` |
| 3 | 1 | Phase | `0`=Move, `1`=Down, `2`=MoveContact, `3`=Up, `4`=Cancel |
| 4 | 4 | Sequence | `uint32`, wraps around; used for reorder/loss detection, not framing |
| 8 | 4 | X | `float32`, `0.0`-`1.0`, normalized within the active drawing/mirror area |
| 12 | 4 | Y | `float32`, `0.0`-`1.0` |
| 16 | 2 | Pressure | `uint16`, `0`-`1024` |
| 18 | 2 | Flags | bitfield: `1`=Primary, `2`=BarrelButton, `4`=Eraser |
| 20 | 8 | Timestamp | `uint64`, client epoch milliseconds |

Floats are sent as their raw IEEE-754 bit pattern (`BitConverter.SingleToUInt32Bits` /
`DataView.setFloat32`), not relied on to match via native struct layout - this is exactly what the
golden-vector test guards against regressing.

The server (`InputPacket.TryDecode`) rejects bad magic, unsupported version, an unknown phase,
NaN/Infinity or out-of-range (with a small tolerance) coordinates, and pressure above 1024. It
never trusts the browser blindly.

A binary frame reaches the host over either transport that's open on the connection: the
WebSocket itself, or (once screen mirroring is active) the `"input"` RTCDataChannel opened
alongside the video track - see [ARCHITECTURE.md](ARCHITECTURE.md#input-transports). Both feed the
same per-connection pipeline and sequence tracker, so packets interleaved from two transports are
handled the same way as packets arriving out of order on one.

## JSON control messages

Every message is a small object with a `"type"` discriminator
([ControlMessages.cs](src/PocketInk.Core/Protocol/ControlMessages.cs)) - never a raw deserialized
arbitrary object.

| `type` | Direction | Fields | Purpose |
|---|---|---|---|
| `hello` | client→server | `protocolVersion`, `clientVersion` | Handshake start. |
| `hello-ack` | server→client | `protocolVersion`, `serverVersion`, `machineName` | Handshake accepted. |
| `heartbeat` | client→server | `clientTimeMs` | Keepalive + RTT measurement. |
| `heartbeat-ack` | server→client | `clientTimeMs` (echoed), `serverTimeMs` | RTT = `now - clientTimeMs`. |
| `hotkey` | client→server | `action` | One of the closed-set `HotkeyAction` values (spec #45) - never arbitrary keys/text. |
| `session` | client→server | `command` (`"stop"`) | Client-initiated clean shutdown of pen/mirror state. |
| `error` | server→client | `message` | Human-readable; also used to reject a bad `hello`. |
| `start-mirror` | client→server | - | Begin WebRTC screen mirroring (Phase 2). |
| `stop-mirror` | client→server | - | End it. |
| `rtc-offer` | server→client | `sdpType` (`"offer"`), `sdp` | WebRTC SDP offer. |
| `rtc-answer` | client→server | `sdpType` (`"answer"`), `sdp` | WebRTC SDP answer. |
| `rtc-ice-candidate` | either direction | `candidate`, `sdpMid`, `sdpMLineIndex`, `usernameFragment` | Trickled ICE candidate; field names match the browser's native `RTCIceCandidateInit` so no translation is needed client-side. |
| `trackpad-move` | client→server | `dx`, `dy` | Relative cursor move (CSS px, already sensitivity-scaled client-side) - trackpad mode only. |
| `trackpad-click` | client→server | `button` (`"left"`\|`"right"`) | A tap-to-click - trackpad mode only. There is no down/up pair on the wire; the host performs the full click atomically. |

Text frames are capped at `ProtocolConstants.MaxControlMessageBytes` (8 KiB); oversized or
malformed ones are dropped rather than crashing the connection.

## Coordinate normalization

`X`/`Y` are always normalized to the *active drawing area*, not the raw phone screen:

- **Tablet mode**: the largest rectangle matching the target monitor's aspect ratio that fits the
  phone's available drawing surface (`TabletAreaCalculator` in C#, mirrored in `app.js`). Touches
  outside it (letterbox padding) don't start a new contact.
- **Mirror mode**: the actual rendered `<video>` content rect under `object-fit: contain`
  (`VideoRectCalculator` in C#, mirrored in `app.js`), since the phone's video element is usually a
  different aspect ratio than the monitor it's showing.

Rotating the phone changes which of width/height is larger, which changes how much letterboxing
the tablet-mode match needs - landscape is usually much closer to a typical monitor's aspect ratio
than portrait. `app.js` listens for both `resize` and `orientationchange` and waits ~200ms before
recomputing, rather than trusting either event's dimensions immediately - iOS Safari can fire them
before the viewport has actually finished settling into the new orientation.

Either way, the host maps the resulting `[0,1]` pair straight onto the target monitor's virtual-
screen bounds (`CoordinateMapper.NormalizedToScreen`), so the same synthetic-pen pipeline serves
both modes without caring which one produced the coordinate.

**Trackpad mode is the exception**: it never uses the binary `InputPacket` at all. Movement is
relative (not tied to any monitor's bounds), so it travels as the small `trackpad-move`/
`trackpad-click` JSON messages above, straight to `SyntheticMouseService.Move`/`Click` - there's no
coordinate mapping step for it to share with the other two modes.
