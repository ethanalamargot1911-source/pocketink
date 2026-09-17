# Latency

## What has and hasn't actually been measured

**Honestly: no rigorous end-to-end ("glass-to-glass") latency measurement has been done for this
project.** That would need a real iPhone, a real Wi-Fi network, and something like a high-speed
camera pointed at both screens simultaneously (the standard way to measure input-to-photon
latency) - none of which were available in this development environment. Any number below is
either a narrowly-scoped measurement of one small piece, or an honest statement that something
hasn't been measured at all. Nothing here should be read as a marketed performance claim.

## What was measured

**WebSocket heartbeat round-trip time**, observed via the phone client's own status bar (which
computes `Date.now() - clientTimeMs` when the `heartbeat-ack` arrives): **1-2 ms**, during
development, with the browser and the PocketInk host running on the *same physical machine*
(loopback / same-host LAN address, not a real two-device Wi-Fi hop). This confirms the WebSocket
plumbing and Kestrel's request handling add negligible overhead on their own - it says nothing
about real Wi-Fi RTT between an iPhone and a PC, which is typically single-digit to low-double-digit
milliseconds on a healthy home network but depends entirely on the router, channel congestion, and
distance, none of which this measurement involved.

## What the heartbeat RTT does *not* capture

The number shown in the phone's status bar is useful as a rough connection-health indicator, but
it measures the round trip of a small JSON message - not the latency a user actually feels when
drawing. The real input-to-pixel path has several more steps the heartbeat never touches:

1. iOS's touch digitizer sampling and Safari's Pointer Events dispatch (device/OS-level, outside
   PocketInk's control).
2. `app.js` encoding and sending the binary `InputPacket` (client-side; not separately measured).
3. Network transit for that specific frame (may differ from the heartbeat's, especially once the
   RTCDataChannel fast path is carrying `MOVE_CONTACT` packets over a different transport - see
   [ARCHITECTURE.md](../ARCHITECTURE.md)).
4. `PenSessionHandler` → `PenInputPipeline` processing (smoothing, pressure, coordinate mapping) -
   deliberately minimal (a single-pole exponential filter, not a multi-frame buffer) specifically
   to avoid adding latency here, but not benchmarked.
5. `SyntheticPenService`'s Win32 `InjectSyntheticPointerInput` call and however long Windows and
   the target application take to actually paint a response - entirely outside PocketInk's control
   and highly dependent on which app has focus.

For screen mirroring, there's a separate, similarly-unmeasured chain: FFmpeg capture → H.264
encode → WebRTC/SRTP transit → browser decode → paint. WebRTC's own statistics (jitter buffer
delay, decode time, etc.) are available through the browser's `RTCPeerConnection.getStats()` API
and were not surfaced anywhere in this project's UI, real or otherwise.

## If you want to actually measure this

- **Rough, no extra hardware:** compare a stopwatch/video recording of a physical Apple Pencil or
  finger tap against how quickly ink appears on the PC screen, over several trials. Crude, but
  honest, and better than trusting the heartbeat number for this purpose.
- **Rigorous:** a high-speed camera (even a phone in slow-motion mode) recording both screens at
  once, timestamping the input event and the visible response frame-by-frame. This is the standard
  method used by real input-latency benchmarks and is the only way to get a number worth quoting.
- **Programmatic:** extend the binary protocol with a server-echoed timestamp per `InputPacket` (a
  natural follow-up to the existing heartbeat pattern) and record deltas over many real strokes -
  not implemented here, but would need no new wire format, just a new message type.

Until one of these is actually done against real hardware, this project makes no latency claim
beyond the loopback heartbeat number above.
