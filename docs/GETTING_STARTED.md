# Getting started

## Requirements

- Windows 10 (10.0.19041 / 20H1) or later, x64.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source.
- An iPhone (or any phone/tablet with a modern browser) on the **same Wi-Fi network** as the PC.
- For screen mirroring only: the FFmpeg 8.1 shared build - see `scripts/install-dependencies.ps1`.
  The drawing-tablet feature works without it.

## Build and run from source

```powershell
git clone <this repo>
cd PocketInk
.\scripts\build.ps1
.\scripts\test.ps1
.\scripts\run.ps1
```

`run.ps1` builds first automatically if it doesn't find an existing build. The PocketInk window
appears with a QR code.

## Pair your phone

1. On the PC: PocketInk's window shows a QR code and, underneath it, "Not paired yet".
2. On the phone: open the Camera app (or any QR scanner) and scan the code. It opens a link like
   `http://192.168.1.50:17462/pair?token=...` in Safari.
3. The page pairs automatically and drops the token from the URL. You should see "Connected" at
   the top of the page within a second or two.
4. Back on the PC, the window updates to "Paired" / "iPhone connected".

If the QR code goes stale (you waited more than 2 minutes without scanning), PocketInk
regenerates it automatically - just scan again.

If this PC has more than one active network connection (real Wi-Fi and a phone hotspot, say) and
the phone can't reach whatever address the QR encoded, use the **Network** dropdown under the QR
code to pick the right one explicitly - it updates the QR immediately. See
[TROUBLESHOOTING.md](TROUBLESHOOTING.md) for why this can happen.

## Try it

- Open any drawing app on the PC (Paint, a browser canvas, whatever you have) and give it focus.
- Draw on the phone screen with your finger. It should draw in the app on the PC.
- Tap "Mirror screen" on the phone to see the PC's screen streamed to the phone instead, and touch
  the video to control the PC through it (same input path, different visual mode - see
  [ARCHITECTURE.md](../ARCHITECTURE.md)).
- Tap again to reach "Trackpad" mode: drag to move the PC's cursor relatively, tap to left-click,
  and use the on-screen "Right click" button for a right-click. One more tap cycles back to
  drawing.
- The **STOP INPUT** button in the PC window immediately releases any active pen contact and held
  modifier keys, regardless of connection state - use it if anything looks stuck.

## Next

- [IPHONE_SETUP.md](IPHONE_SETUP.md) - more detail on the phone side, including what to do if
  scanning doesn't work.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - common problems and fixes.
- [../SECURITY.md](../SECURITY.md) - what this trusts and what it doesn't.
