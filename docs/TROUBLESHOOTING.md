# Troubleshooting

## PocketInk window doesn't open, or crashes on startup

If a fresh build crashes with `System.InvalidOperationException: Cannot find non-neutral culture
related to 'en-us'` (thrown from `System.Windows.Markup.XmlLanguage.GetSpecificCulture`), that's
`InvariantGlobalization` being enabled in `PocketInk.Host.csproj` - WPF's data-binding engine needs
real culture data and breaks under invariant mode. This project's `.csproj` already has it disabled
(`<InvariantGlobalization>false</InvariantGlobalization>`) after hitting exactly this during
development; if you've re-enabled it (e.g. chasing publish size), that's why the window won't open.

## "Not paired" won't go away after scanning

- **The QR code expired.** Tokens are single-use and expire after 2 minutes if unscanned. Just
  scan the current code - PocketInk regenerates it automatically once the old one expires, so an
  old screenshot or a code you scanned a while ago won't work.
- **The QR encodes the wrong network address.** If this PC has more than one active network
  connection at once (e.g. real Wi-Fi *and* a phone hotspot, or Wi-Fi *and* Ethernet), PocketInk
  has to guess which one the phone can actually reach, and that guess isn't always stable across
  restarts when several are up simultaneously - confirmed directly during development, where the
  QR pointed at a stale Wi-Fi address after switching to a hotspot. Use the **Network** dropdown
  under the QR code to pick the right connection explicitly; it updates the QR code immediately, no
  restart needed. "(Automatic)" is the default guess-based behavior.
- **Different networks.** Confirm the phone and PC are on the *same* Wi-Fi network, not one on
  Wi-Fi and one on cellular/a guest network/a different SSID. Client isolation (some guest
  networks and mesh routers block device-to-device traffic even on the same SSID) will look
  exactly like this from the phone's side - try a different network if you suspect this. A public
  Wi-Fi network you don't control (campus, coworking space, café) very often has this - a personal
  phone hotspot sidesteps it entirely and is the safer choice on a network you don't administer
  (see [../SECURITY.md](../SECURITY.md)).
- **A network marked "Public" by Windows blocks inbound connections by default.** Confirmed during
  development on a campus Wi-Fi network: Windows Firewall silently drops unsolicited inbound
  connections on networks it categorizes as Public, with no prompt at all (the prompt you may be
  picturing only appears on Private networks). Check with PowerShell:
  `Get-NetConnectionProfile | Select-Object Name, NetworkCategory`. Opening a firewall port on a
  genuinely public/shared network means anyone else on it can reach PocketInk's HTTP endpoints
  (the pairing token still protects who can actually pair) - a personal hotspot avoids this
  tradeoff entirely, which is why it's the safer fix here too.
- **Windows Firewall (Private networks).** The first time Kestrel binds to a LAN-facing port on a
  network Windows considers Private, it may prompt to allow the app through. If you clicked
  "Cancel", the phone can't reach the PC at all (not just pairing - nothing will load). Check
  Windows Defender Firewall's allowed-apps list if pairing fails and everything else above checks
  out.

## Screen mirroring says "unavailable" or never shows video

This means `FFmpegAvailability.EnsureInitialised` failed - FFmpeg's native libraries weren't found.
The drawing tablet still works; this only affects "Mirror screen".

1. Run `scripts/install-dependencies.ps1`, which installs the FFmpeg 8.1 shared build via winget.
2. **Version matters.** `SIPSorceryMedia.FFmpeg` 10.0.16 targets the FFmpeg **8.1** ABI
   specifically. During development, installing FFmpeg 9.0.1 (the winget default at the time)
   failed silently; downgrading to exactly 8.1 fixed it. If you already have a different FFmpeg
   version installed and mirroring doesn't work, that version mismatch is the most likely cause -
   check with `ffmpeg -version` (look for `ffmpeg version 8.1...` near the top).
3. **You just installed it and it still doesn't work.** A `winget install` doesn't always update
   the *current* process's `PATH` immediately. PocketInk already has a fallback for this - it
   searches the well-known winget install location
   (`%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared*`) if `PATH` doesn't resolve it -
   but if you installed FFmpeg somewhere else entirely, either add it to `PATH` and restart
   PocketInk, or move/symlink it under that winget path pattern.
4. Only the **shared** build works (`avcodec-*.dll`, `avformat-*.dll`, etc. as separate files).
   The plain `ffmpeg.exe`/`ffprobe.exe` CLI-only ("full_build", non-shared) distribution does not
   include these and will not satisfy this dependency even if `ffmpeg.exe` itself runs fine from a
   terminal.

## Drawing works but strokes land in the wrong place

- Check the **target monitor** dropdown in the PocketInk window - input maps to whichever monitor
  is selected there, which may not be the one you're looking at on a multi-monitor setup.
- If you just changed monitors or resolution, the phone's tablet-area calculation is based on the
  monitor's aspect ratio fetched from `/api/tablet-config` when the page loads - reload the phone
  page after changing the target monitor.

## A stroke or key seems "stuck"

Click **STOP INPUT** in the PocketInk window. It unconditionally cancels any active synthetic pen
contact and releases every held modifier key, independent of whatever the connection thinks its
state is. If this doesn't fix it, the stuck state likely isn't coming from PocketInk's synthetic
pointer at all (e.g. a real physical key genuinely held down).

## Port already in use

PocketInk tries the configured port (default `17462`) and then nine more ports above it before
giving up. If all ten are taken, it throws on startup - free up a port or change
`WebServerPort` in settings (`%LOCALAPPDATA%\PocketInk\settings.json`) and restart.

## The phone shows an old version of the page after an update

Safari can cache `app.js`/`protocol.js` aggressively. Force-reload the page (or clear Safari's
website data for the PC's IP) if you've rebuilt PocketInk and the phone doesn't seem to reflect a
client-side change.
