# iPhone setup

> **Honesty note:** everything in this file describes the intended behavior based on how the web
> client is built and on Safari/WebKit's documented Pointer Events and WebRTC support. It has
> **not** been verified against a real iPhone during development - only against a desktop browser
> (which supports the same web APIs, but isn't the same touch hardware or the same WebKit build).
> If something here doesn't match what you see on an actual device, trust the device and please
> treat this as a bug report waiting to happen.

## Requirements

- iOS Safari (or any browser using WebKit on iOS, since iOS requires all browsers to use WebKit).
  No app install, no App Store, no Xcode involved anywhere.
- The same Wi-Fi network as the PC. PocketInk does not work over cellular, a different Wi-Fi
  network, or a VPN that routes around the LAN.
- Nothing needs to be installed on the phone. The "client" is just the page PocketInk's own web
  server serves - there is no separate app.

## Pairing

Scan the QR code shown in the PC's PocketInk window with the iPhone's Camera app (it recognizes
QR codes natively and offers to open the link - no scanner app needed). If Camera doesn't offer to
open it, or you'd rather not use Camera, any QR scanner app that opens the resulting URL in Safari
will work the same way.

## What to expect

- **Drawing tablet mode** (default): the whole screen is your drawing surface. It's automatically
  letterboxed to match the target monitor's aspect ratio, so a 1:1 stroke on the phone maps to a
  proportionally correct stroke on the monitor - touching in the padding on either side doesn't
  start a stroke.
- **Mirror mode** (tap "Mirror screen"): the PC's screen streams to the phone over WebRTC; touching
  the video controls the PC through the same underlying input path.
- The status bar at the top shows connection state and round-trip latency (updated every 2
  seconds via a heartbeat).
- Backgrounding Safari, switching apps, or locking the phone immediately tells the PC to release
  any in-progress stroke and held keys - it doesn't wait for a timeout (see
  [../SECURITY.md](../SECURITY.md)).

## Known iOS/Safari-specific considerations (untested assumptions)

- `getCoalescedEvents()` (used to capture every point Safari's touch digitizer sampled, not just
  the ones that produced a `pointermove` DOM event) is a real API on iOS Safari, but its exact
  sampling rate on-device hasn't been measured here.
- `playsinline` is set on the mirror `<video>` element specifically to stop iOS from taking over
  the whole screen with its native fullscreen video player - this is a well-known iOS requirement,
  not a guess, but hasn't been confirmed on-device in this project.
- Apple Pencil is not specifically handled. Pointer Events should report it as
  `pointerType: "pen"` with real pressure data if used on a compatible iPad, but PocketInk always
  sends a `pointerType`-independent packet and hasn't been tested with a Pencil at all.
- Screen Time / Guided Access / battery-saver features that throttle background tabs could affect
  the heartbeat and reconnect behavior; not tested.

If you do test this on a real device, the things most worth checking first are: does a stroke
actually land in the right place (coordinate mapping), does the STOP INPUT button actually recover
from a stuck contact if you force-quit Safari mid-stroke, and does mirror mode's video actually
play with `playsinline` rather than going fullscreen.
