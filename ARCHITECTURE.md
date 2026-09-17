# Architecture

## Repo layout

```
PocketInk.sln
src/
  PocketInk.Core/     Pure, OS-independent logic - no Win32, no ASP.NET Core, no WPF.
  PocketInk.Host/      The actual Windows app: WPF window + Kestrel web server in one process.
tests/
  PocketInk.Tests/     xUnit tests, almost all against PocketInk.Core or a real-but-safe Kestrel instance.
  js/                  Node.js golden-vector test for the browser-side binary protocol encoder.
scripts/               build / test / run / publish / install-dependencies (all PowerShell).
docs/                  GETTING_STARTED, IPHONE_SETUP, TROUBLESHOOTING, LATENCY.
```

The Core/Host split exists so the parts that touch real state (Win32 pointer injection, the
filesystem, `System.Windows.Forms.Screen`, native FFmpeg) are thin adapters over pure functions
that `dotnet test` can actually exercise. Examples: `CoordinateMapper`/`TabletAreaCalculator`/
`VideoRectCalculator` (Core, pure math) vs. `MonitorService` (Host, wraps `Screen.AllScreens`);
`LanAddressSelector` (Core) vs. `LanAddressProvider` (Host, wraps `NetworkInterface`);
`PenWatchdogLogic`-style pure decision functions vs. the real `SyntheticPenService`.

## One process, two jobs

`App.xaml.cs` starts both halves on launch: a Kestrel server (`WebHostService`) serving the phone
client from `wwwroot/` and the WebSocket endpoint, and the WPF `MainWindow` that shows the pairing
QR and lets you change settings. They share state directly through `HostServices` and
`PairingService` (both exposed as `App.Host` / `App.Pairing`) - no IPC, because there's no process
boundary to cross.

### Which network address the QR code uses

`LanAddressSelector.SelectPreferredAddress` picks the first up, non-loopback, non-link-local
adapter it finds when no preference is set - and when more than one qualifies (real Wi-Fi *and* a
phone hotspot both up at once, say), which one comes first isn't guaranteed stable across restarts.
This was observed directly during development: the QR pointed at a stale Wi-Fi address right after
switching to a hotspot. The **Network** dropdown in `MainWindow` (backed by
`AppSettings.PreferredNetworkAdapterId`) lets the user pin the right adapter explicitly, and takes
effect immediately by re-calling `PairingService.Initialize` and regenerating the QR - no restart
needed, unlike most other settings here.

## Phase 1: drawing tablet

```
iPhone Safari (Pointer Events)
  → binary InputPacket over WebSocket
  → PenSessionHandler (per-connection)
  → PenInputPipeline (per-connection: sequence tracking, smoothing, pressure)
  → CoordinateMapper (normalized [0,1] → target monitor's screen pixels)
  → SyntheticPenService (Win32 CreateSyntheticPointerDevice / InjectSyntheticPointerInput, PT_PEN)
  → whatever app has focus on the PC
```

A fresh `PenInputPipeline` (sequence tracker, point smoother, velocity/pressure simulator) is
created per WebSocket connection, so reconnecting never leaks state from the previous session.
`SyntheticPenService` and `PenWatchdog`, by contrast, are singletons on `HostServices` - there's
only one real synthetic pen device for the whole process, matching there being one phone connected
at a time.

## Phase 2: interactive screen mirroring

```
FFmpegScreenSource (gdigrab capture of the target monitor's exact bounds)
  → FFmpeg H.264 encode
  → RTCPeerConnection.SendVideo
  → WebRTC (SRTP) over the LAN
  → <video> element in the browser
```

gdigrab doesn't draw the mouse cursor by default, which made the mirrored screen look like nothing
was ever pointing at anything - particularly awkward once trackpad mode is being used to control
it. `ScreenMirrorSession` explicitly enables it via `FFmpegScreenSource.InitialiseDecoder(new
Dictionary<string,string> { ["draw_mouse"] = "1" })`, confirmed empirically (captured a frame with
the cursor moved to a known position and visually verified it appears) rather than assumed.

Signaling (offer/answer/ICE) rides the *same* WebSocket as Phase 1, as small JSON messages (see
[PROTOCOL.md](PROTOCOL.md)) - there's no separate signaling server. `ScreenMirrorSession` owns one
`RTCPeerConnection` + `FFmpegScreenSource` per mirroring session and is created lazily on the
phone's first `start-mirror` message, so a machine without FFmpeg installed still has a fully
working Phase 1 (`FFmpegAvailability.EnsureInitialised` fails soft, sends an `error` message, and
never touches Phase 1's code path).

### Input transports

Touching the mirrored video sends the exact same binary `InputPacket` frames as the drawing
tablet - `VideoRectCalculator` just computes a different normalized coordinate (accounting for the
video's letterboxing) before handing off to the same `CoordinateMapper` → `SyntheticPenService`
chain. Once a mirroring session is active, the host also opens an unreliable/unordered
`RTCDataChannel` (label `"input"`) on the same peer connection; the browser sends `MOVE_CONTACT`
packets over it when open (falling back to the WebSocket otherwise) for lower latency, while
Down/Up/Cancel stay on the WebSocket where reliability matters more. Both transports decode into
the *same* `PenInputPipeline` for that connection, so its sequence-number-based reordering already
handles packets arriving out of order across two transports - no separate merge logic needed.

## Trackpad mode (optional third mode)

```
iPhone Safari (single-finger drag / tap / on-screen right-click button)
  → JSON trackpad-move / trackpad-click over the WebSocket
  → PenSessionHandler
  → SyntheticMouseService (Win32 SendInput, relative MOUSEINPUT)
  → wherever the real OS cursor currently is
```

Unlike the tablet and mirror modes, this is *relative* movement - it doesn't touch
`CoordinateMapper` or any monitor's bounds at all, and doesn't use the binary `InputPacket` wire
format. It's also the simplest of the three: a single-finger drag moves the cursor, a
tap-without-drag is a left click, and right-click is a dedicated on-screen button rather than a
two-finger gesture (which would need tracking a second simultaneous touch that the rest of this
client's "one active contact" model doesn't support). There's no drag-to-select and no scroll -
deliberately out of scope for a first pass at a feature the original spec called optional.
`SyntheticMouseService.Cancel()` force-releases any held button on every disconnect, the same
"never leave a stuck input" policy `PenWatchdog` and `HotkeyInjector.ReleaseAllModifiers()` apply
to the other two modes.

### Adaptive bitrate

When `AppSettings.VideoQuality` is `Auto`, `ScreenMirrorSession` listens to real RTCP receiver
reports (`pc.OnReceiveReport`) and adjusts the encoder's target bitrate via
`BitrateAdaptationCalculator` (Core, pure, tested): back off hard on real loss, hold steady on
mild loss, ramp up slowly on a clean link, bounded to fixed Low/High presets
(`VideoQualityPresets`). Low/Balanced/High are otherwise fixed - "Auto" is the only mode that
reacts to measured network conditions.

## Testing policy

**No automated test ever sends a real binary `InputPacket` through the live pipeline**, because
`SyntheticPenService.PenMove` starts a real contact (`DOWN` flags) even without a preceding
`PenDown`, and `PenDown`/`PenMove`/`PenUp` all call the real Win32
`InjectSyntheticPointerInput` - there is no code path from a decoded, in-range `InputPacket` to
anything other than a genuine synthetic pointer event on whatever machine is running the test.
That's true whether the packet arrives over the WebSocket or the data channel; both decode through
the same function.

Concretely, this means:

- `WebSocketHandshakeTests.cs` and `HttpEndpointsTests.cs` exercise a real Kestrel instance and a
  real `ClientWebSocket`/`HttpClient`, but only ever send **text** control messages
  (hello/heartbeat/pairing) - never a binary frame.
- The one place real pointer injection is ever triggered outside a live phone connection is the
  host UI's **Test Pen** button, which requires an explicit user click plus a confirmation dialog.
  It has never been invoked by an automated test or by an AI agent driving the app - only by a
  human, on purpose, after being told exactly what it's about to do.
- Screen *capture* (reading the display) carries no such risk and was verified live during
  development - launching the real host, pairing a real browser tab, and confirming actual video
  frames render - because it has no side effect on the desktop beyond reading pixels.
- WebRTC data-channel *negotiation* was likewise verified live (confirming the channel actually
  opens), without ever sending a real `InputPacket` through it, for the same reason.
- The same restriction applies to trackpad mode's `trackpad-move`/`trackpad-click` messages -
  they reach `SyntheticMouseService.Move`/`Click`, which move the real OS cursor and can click
  real UI elements, exactly the same class of risk as PT_PEN injection. The three-mode toggle
  cycle (Draw → Mirror → Trackpad) was verified live by clicking the mode button itself and
  confirming labels/visibility changed correctly - but never by dragging on the trackpad surface
  or pressing its on-screen right-click button, since either would genuinely move the mouse and
  click whatever was under it on the real desktop running the test.

If you're adding a test that touches `PenInputPipeline.ProcessPacket`, `SyntheticPenService`, or
anything downstream of them with a valid, in-range packet, stop and reconsider - that's exactly
the thing this policy exists to prevent.
