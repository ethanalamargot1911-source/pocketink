# PocketInk

Turns an iPhone (or any phone/tablet with a modern browser) into a low-latency graphics tablet,
an interactive mirrored/touchable display, for a Windows PC - with no Mac, no Xcode, and no native
iOS app. The phone client is a page served directly by the Windows host over your LAN; you pair by
scanning a QR code. No cloud, no Bonjour, no account.

```
┌─────────────┐   Wi-Fi, same LAN    ┌──────────────────────────────┐
│  iPhone     │ ◄──── WebSocket ────► │  PocketInk Host (Windows)    │
│  Safari     │       + WebRTC        │  WPF window + Kestrel server │
│  (no app)   │                       │  → synthetic PT_PEN input    │
└─────────────┘                       └──────────────────────────────┘
```

## Features

- **Drawing tablet** - draw on the phone, ink appears in whatever app has focus on the PC, mapped
  to a target monitor with correct aspect-ratio matching.
- **Interactive screen mirroring** - see the PC's screen on the phone (WebRTC/H.264) and control it
  by touching the video, using the same input path as the tablet.
- **Trackpad** (optional third mode) - drag to move the PC's cursor relatively, like a real
  trackpad, tap to left-click, a dedicated button for right-click.
- **Pairing by QR code** - scan, done. Tokens are single-use and expire in 2 minutes; sessions
  persist until you explicitly forget the device.
- **Fail-safe by construction** - a watchdog force-releases a stuck contact, backgrounding the
  phone cancels in-progress input immediately, and a STOP INPUT button in the host UI is always one
  click away. See [SECURITY.md](SECURITY.md).

## Quick start

```powershell
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

Then scan the QR code PocketInk shows with your phone. Full walkthrough:
[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md).

Screen mirroring additionally needs the FFmpeg 8.1 shared build:
`.\scripts\install-dependencies.ps1`. The drawing tablet works without it.

## How it's built

- **C# / .NET 8**, WPF for the host window, ASP.NET Core Kestrel embedded in the same process for
  the web server and WebSocket endpoint, Win32 P/Invoke for synthetic pointer injection.
- **SIPSorcery** / **SIPSorceryMedia.FFmpeg** for WebRTC and screen capture/H.264 encode.
- **No native iOS app, no Xcode.** The phone client is plain HTML/CSS/JS served from `wwwroot/`.
- `src/PocketInk.Core` holds pure, OS-independent logic (coordinate math, protocol encoding,
  pairing token rules); `src/PocketInk.Host` is the Windows-specific app built on top of it. See
  [ARCHITECTURE.md](ARCHITECTURE.md) for the full picture, including how input and video flow
  through the system and why tests are structured the way they are.

## Documentation

| Doc | Covers |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | How the pieces fit together, Phase 1 vs Phase 2 data flow, testing policy |
| [PROTOCOL.md](PROTOCOL.md) | The exact wire format - binary `InputPacket` layout and JSON control messages |
| [SECURITY.md](SECURITY.md) | The trust model: what's protected, what isn't, and why |
| [LICENSES.md](LICENSES.md) | Third-party dependencies, including an important note on FFmpeg's license |
| [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) | Build, run, pair, first stroke |
| [docs/IPHONE_SETUP.md](docs/IPHONE_SETUP.md) | The phone side in more detail |
| [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Common problems, including real ones hit during development |
| [docs/LATENCY.md](docs/LATENCY.md) | What's actually been measured (honestly, not much) vs. what hasn't |

## Status

Both phases work end-to-end and have been verified against a real running host - drawing input
injection, pairing, and WebRTC screen mirroring (including its data-channel fast path and
adaptive-bitrate logic) were each confirmed live during development, not just built. What has
**not** been verified is any of it against an actual iPhone - development and live testing used a
desktop browser throughout, since no physical iPhone was available. See
[docs/IPHONE_SETUP.md](docs/IPHONE_SETUP.md) and [docs/LATENCY.md](docs/LATENCY.md) for exactly
what that does and doesn't mean in practice.

## License

Not yet declared for PocketInk's own code. See [LICENSES.md](LICENSES.md) for third-party
dependencies and an important note on the FFmpeg build this project was tested against.
