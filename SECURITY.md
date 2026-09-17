# Security model

PocketInk injects synthetic keyboard/pointer input into whatever has focus on your PC, from a
device that only needs to be on the same Wi-Fi network. That's a meaningful trust boundary, so
here's exactly what does and doesn't protect it.

## Design constraints

- **LAN-only, no cloud, no relay - for Local mode.** The host binds Kestrel to all local
  interfaces on a configurable port (default `17462`, auto-falling-back through `+9` if taken) and
  is only ever reachable by something that can already reach your LAN. There is no external
  server in the loop, no account, no telemetry. This is the default and the recommended mode.
  **Remote mode is the deliberate, opt-in exception** to every claim in this bullet - see
  "Remote pairing" below before enabling it.
- **Plain HTTP/WS, not HTTPS/WSS, on purpose.** A self-signed cert would just produce a scary
  browser warning on every pairing for no real gain on a trusted home LAN, and getting a real cert
  for a device with no public DNS name isn't practical. The session cookie is `Secure=false`
  *deliberately* - see below.
- **No Bonjour/mDNS discovery.** Pairing happens by scanning a QR code that encodes the host's
  actual LAN IP, not by broadcasting or discovering anything.

## Pairing

- The QR code encodes a **single-use, 128-bit random token** with a 2-minute expiry
  (`PairingToken`, `ProtocolConstants.PairingTokenLifetime`). It's compared with a fixed-time
  comparison (`CryptographicOperations.FixedTimeEquals`), not `==`.
- A successful pair exchanges the token for a separate, long-lived random session value (256 bits
  of entropy), delivered as an `HttpOnly`, `SameSite=Strict` cookie. `HttpOnly` keeps it out of
  reach of any JS running on the page (including a compromised/malicious script); `SameSite=Strict`
  stops another site from riding a request to it.
- The displayed QR auto-refreshes to a fresh token if the old one expires unscanned, so a stale
  screenshot of the pairing screen goes stale for real, not just visually.
- **"Forget paired iPhone"** revokes every active session immediately and rotates the token.
  **"Refresh pairing QR"** rotates the token without touching existing sessions (e.g. if you think
  the QR code itself might have been seen by someone else, but the phone that's already paired
  should stay paired).

## Remote pairing (cross-network, via Supabase)

Remote mode exists for when the phone isn't on the PC's own network. It's off by default (needs a
Supabase URL, anon key, and a hosted client URL filled in on the host UI before the "Generate
remote pairing QR" button does anything) and changes the trust model in ways Local mode doesn't -
read this before turning it on.

- **The pairing code is the entire trust boundary.** Local mode has an implicit second factor -
  whoever pairs has to already be on your Wi-Fi. Remote mode has no equivalent: the code is a
  128-bit random value (`PairingToken`, reused as `SupabasePairingSession.PairingCode`), but anyone
  who obtains it, from anywhere on the internet, can attempt to pair for as long as the window
  below stays open. Treat a Remote QR code like a temporary password, not like the LAN QR (which is
  only useful to someone already inside your home).
- **The window is short but real: up to 30 seconds, and only one taker.** The host waits up to
  `SupabasePairingSession`'s connect timeout (30s) for a phone to complete the WebRTC handshake
  over the named Supabase Realtime Broadcast channel, then tears the channel down
  (`RemotePairingService`/`SupabasePairingSession.DisposeAsync`) whether it succeeded, failed, or
  timed out - a code can't be reused for a second attempt once that happens. Within that window,
  though, Supabase Realtime does no additional authorization beyond "knows the channel name and the
  project's anon key" - if two clients race to answer the host's offer, whichever gets there first
  wins, not necessarily the phone the code was meant for.
- **Supabase sees connectivity metadata for that window, never drawing/video content.** The only
  things relayed through its Broadcast channel are a WebRTC SDP offer/answer and trickled ICE
  candidates (`SignalRelayPayload`) - network addresses and codec/capability negotiation, not
  pen strokes, screen video, or trackpad movements. Once the "control" `RTCDataChannel` opens,
  Supabase is never contacted again for the rest of that session (see `RemotePairingService`'s own
  doc comment). Supabase's own servers can still see each client's public IP address and connection
  timing, the same as any realtime relay a browser or app connects to.
- **A TURN provider, if one is configured and actually needed for a given connection, sees
  encrypted traffic volume and timing, not content.** All PocketInk traffic - screen video, pen
  input, trackpad, hello/heartbeat/hotkeys - rides WebRTC's mandatory DTLS-SRTP encryption whether
  it goes host-to-phone directly or through a TURN relay. A relay that's actually in the path can
  see packet sizes and timing (which can weakly hint at "video is playing" vs. "idle"), and both
  peers' public IP addresses, but not the video frames, keystrokes, or coordinates themselves.
  Both Supabase and the TURN provider are new parties in the trust chain that Local mode never
  involves at all - that's the real cost of working across networks, not a bug to route around.
- **The Supabase anon/publishable key in the QR's URL is not a leak.** The Remote QR encodes
  `?connect=<code>&su=<supabase-url>&sk=<anon-key>`. That key is Supabase's public, RLS-scoped
  client credential - the one meant to ship inside client-side apps - not the secret/service-role
  key, which never appears anywhere in PocketInk. Anyone who could derive it from the QR could
  equally have gotten it from the Supabase project's own client-side JS if they had any other way
  to reach it; it grants no more access than "can send/receive Realtime broadcasts on whatever
  channel name it also knows," which is exactly what the pairing code above already assumes.
- **This reuses `PenSessionHandler` unchanged.** Once the control data channel is open, a Remote
  session is driven by the exact same code as a Local one (`IControlChannel` abstracts the
  transport) - the closed hotkey set, `InputPacket` validation, and every fail-safe in the next two
  sections apply identically. Remote mode only changes *how the phone and PC find each other*, not
  what happens once they have.

## The control channel (WebSocket or WebRTC data channel)

`PenSessionHandler` dispatches messages the same way regardless of which `IControlChannel`
delivers them - a Local-mode WebSocket or a Remote-mode `RTCDataChannel` - so everything below
applies to both, except the first bullet, which is specifically how Local mode's WebSocket upgrade
is gated (Remote mode's equivalent gate is the pairing code itself, covered above).

- The `/ws` upgrade is rejected unless: it's a genuine WebSocket request, the `Origin` header
  (when the browser sends one) matches the host's own origin, and the pairing session cookie is
  present and valid. Any one of those failing is a `400`/`401`/`403`, not a silent fallback.
- **Hotkeys are a closed set** (`HotkeyAction`: Undo, Redo, Space, Escape, RightClick, Ctrl/Shift/Alt
  down-up). The client can never send an arbitrary keycode or string of text to be typed - there is
  no code path from a JSON message to "type these characters."
- Every binary frame is validated before it touches anything (`InputPacket.TryDecode`): bad magic,
  wrong version, unknown phase, NaN/Infinity/out-of-range coordinates, and out-of-range pressure
  are all rejected outright.

## Fail-safe behavior

Nothing here is enforced by the network layer alone - it's backed by explicit "never leave a
stuck contact or held modifier" logic, because that's the failure mode that would actually hurt:

- `PenWatchdog` force-releases the synthetic pen contact if no valid input arrives for
  `ProtocolConstants.PenWatchdogTimeout` (500 ms) while a contact is active - a dropped connection
  can't leave a phantom "mouse button held down" on your PC.
- The browser client sends a `Cancel` packet and a `session: stop` message on `visibilitychange`
  (tab hidden), `pagehide`, and `beforeunload` - closing the tab or switching apps on the phone
  cleans up immediately rather than waiting for the watchdog timeout.
- Every disconnect path (`PenSessionHandler`'s `finally` block) unconditionally cancels any active
  contact, releases all held modifier keys, releases any held trackpad mouse button, and disarms
  the watchdog - not just the "clean" disconnect path.
- **STOP INPUT** in the host UI is a manual panic button that does the same cancel-and-release
  immediately, independent of any connection state.
- **Test Pen** (also host UI) is the only way real synthetic input is ever triggered by anything
  other than a live phone connection, and it requires an explicit click plus a confirmation dialog
  describing exactly what it's about to do. It is never invoked automatically or by an agent/test
  harness - see the note in [ARCHITECTURE.md](ARCHITECTURE.md#testing-policy).

## What this does *not* protect against

Being honest about the boundaries:

- **Anyone else on your LAN who intercepts the QR code within its 2-minute window can pair.**
  There's no additional PIN/confirmation step on the PC side beyond the QR scan itself. On a
  trusted home network this is an acceptable tradeoff for zero-friction pairing; on a shared or
  untrusted network (coworking space, hotel Wi-Fi) it is not, and PocketInk should not be paired
  there.
- **Plain HTTP means anyone on the LAN who can see the traffic can see it.** There's no
  confidentiality against a local network eavesdropper (e.g. another device on a compromised
  router). This is the direct tradeoff for skipping TLS - see "Plain HTTP/WS" above.
- **No brute-force rate limiting on `/api/pair`.** A 128-bit token isn't guessable in the 2-minute
  window regardless, so this hasn't been treated as a priority, but it also hasn't been load-tested
  as a denial-of-service surface.
- **Multiple devices can hold valid sessions simultaneously** if you pair more than one phone
  (nothing currently limits it to one). "Forget paired iPhone" revokes *all* of them at once, not
  a specific one, since sessions aren't currently tracked per-device beyond the cookie value.
- **Anyone who obtains a Remote pairing code within its ~30-second window can pair, from anywhere
  on the internet - not just your LAN.** This is the whole tradeoff of Remote mode, spelled out in
  "Remote pairing" above; there's no PIN or confirmation step there either. Don't screen-share, post,
  or otherwise expose a Remote QR code the way you might casually show the Local one to someone
  standing next to you.
- **No rate limiting or abuse protection on the Supabase project itself.** Anyone who knows (or
  guesses, though a 128-bit code isn't guessable) a channel name can attempt to join it; Supabase's
  own project-level quotas are the only backstop against someone hammering the Realtime endpoint.
