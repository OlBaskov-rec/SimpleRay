# Third-party notices

SimpleRay is licensed under the GNU General Public License v3.0 (see [LICENSE](LICENSE)).
It uses and, in its release artifacts, redistributes the components below. This file
records their origin and license so those obligations are met.

## Bundled at runtime (in release archives, under `core/` and `geo/`)

These are **not** stored in this repository; `scripts/fetch-deps.ps1` downloads them from
their upstream sources (with hash / Authenticode verification) into the release archive.

| Component | Source | License |
|-----------|--------|---------|
| `sing-box.exe` | [SagerNet/sing-box](https://github.com/SagerNet/sing-box) | **GPL-3.0-or-later** |
| `wintun.dll` | [wintun.net](https://www.wintun.net/) (WireGuard LLC) | Wintun prebuilt-binary license (verbatim redistribution of the unmodified binary is permitted) |
| `geoip-*.srs` | [SagerNet/sing-geoip](https://github.com/SagerNet/sing-geoip) | See upstream repository |
| `geosite-*.srs` | [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite) | See upstream repository |

sing-box is invoked as a separate child process (via a config file and OS signals — no
linking), so SimpleRay's own code is not a derivative work of it. Because release archives
redistribute the unmodified `sing-box.exe`, its GPL-3.0 terms apply to that binary: its
license text and corresponding source are available from the upstream project above.

## NuGet dependencies (compile/runtime)

| Package | License |
|---------|---------|
| [ZXing.Net](https://github.com/micjahn/ZXing.Net) | Apache-2.0 |
| [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) | CPOL-1.02 |
| System.Security.Cryptography.ProtectedData | MIT (.NET Foundation) |
