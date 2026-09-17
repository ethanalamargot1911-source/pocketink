# Third-party licenses

PocketInk itself has no license declared yet — that's a decision for the project owner, not
something this document invents. This file covers the third-party components it depends on.

## Bundled in the built application (NuGet packages)

| Package | Version | License | Notes |
|---|---|---|---|
| [QRCoder](https://www.nuget.org/packages/QRCoder) | 1.6.0 | MIT | Generates the pairing QR code PNG. |
| [SIPSorcery](https://www.nuget.org/packages/SIPSorcery) | 10.0.16 | BSD-3-Clause | WebRTC peer connection, SDP, ICE, RTP/RTCP. |
| [SIPSorceryMedia.Abstractions](https://www.nuget.org/packages/SIPSorceryMedia.Abstractions) | (transitive) | BSD-3-Clause | Interfaces `SIPSorceryMedia.FFmpeg` implements. |
| [SIPSorceryMedia.FFmpeg](https://www.nuget.org/packages/SIPSorceryMedia.FFmpeg) | 10.0.16 | **LGPL-2.1-only** | Screen capture + H.264 encode via native FFmpeg. See the FFmpeg section below - this is the one dependency that needs care. |
| [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen) | (transitive) | MIT/Unlicense (dual) | P/Invoke bindings `SIPSorceryMedia.FFmpeg` is built on. |

Test-only packages (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`,
`xunit.runner.visualstudio`, `Microsoft.Extensions.Logging.Abstractions`) never ship in the
published Host executable and aren't listed here for that reason.

## Not bundled: FFmpeg itself

`SIPSorceryMedia.FFmpeg` wraps FFmpeg's native shared libraries (`avcodec`, `avformat`,
`avutil`, `swscale`, `swresample`) but does not include them - `scripts/install-dependencies.ps1`
installs them separately on the target machine, and `scripts/publish.ps1` does not copy them into
the publish output. This project was built and tested against:

- **[Gyan.FFmpeg.Shared](https://www.gyan.dev/ffmpeg/builds/) 8.1** ("full_build-shared")

**This specific build is licensed GPL-3.0, not LGPL.** Its own reported build configuration
includes `--enable-gpl --enable-version3 --enable-libx264 --enable-libx265` - the H.264/H.265
encoders PocketInk actually uses for screen mirroring are GPL-licensed (`libx264`/`libx265`), and
Gyan's "full" builds compile them in. `SIPSorceryMedia.FFmpeg`'s own README assumes the *default*
FFmpeg build is LGPL and asks integrators to "make sure that's acceptable for your application" -
for this specific build, it is not strictly LGPL, and that's worth being explicit about rather
than repeating the upstream assumption uncritically.

Why this doesn't obligate PocketInk's own source to be GPL: FFmpeg is never bundled or
redistributed by this project. The end user installs their own separate copy via
`scripts/install-dependencies.ps1` (a thin `winget install` wrapper), and PocketInk loads it at
runtime via `SIPSorceryMedia.FFmpeg`'s P/Invoke bindings. This is the same "the user brings their
own FFmpeg" pattern used by many other applications that shell out to or dynamically load FFmpeg
without shipping it. It is a reasonable position, not a certainty - if you plan to **redistribute**
PocketInk bundled together with FFmpeg binaries (which the current build/publish scripts do not
do), get real legal advice before doing so; GPL's terms for combined/distributed works are more
involved than a two-paragraph summary can responsibly cover.

If you'd rather avoid the GPL question entirely, an LGPL-only FFmpeg build (e.g. one of the
`BtbN.FFmpeg.LGPL.Shared.*` packages on winget) should also satisfy `SIPSorceryMedia.FFmpeg`'s
runtime requirements, at the cost of losing `libx264`/`libx265` and falling back to FFmpeg's
built-in (non-GPL) encoders where available - **this has not been tested** by this project; only
the Gyan 8.1 GPL shared build has been verified to actually work end-to-end (see
[docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)).
