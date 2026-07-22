# UI screenshots

History of the interface, one folder per released version (`vMAJOR.MINOR.PATCH`).
Add a new set **only when the UI changes**; otherwise the previous version's shots
still apply.

Conventions:
- Compact but legible: downscaled to ~720 px wide, PNG (crisp text).
- Name shots by window/state: `main-window.png`, `app-routing.png`, `webcam-qr.png`, …
- Captured from the real build via Win32 `PrintWindow` (PW_RENDERFULLCONTENT), so
  they reflect exactly what ships.

## v0.2.0
- `main-window-en.png` / `main-window-ru.png` — main window (English / Russian):
  profile list with failover-group checkboxes and the "certificate not verified"
  warning, routing panel, failover group selector, all import actions
  (clipboard / file / QR ×3 / subscription / refresh), and the footer with
  version, **language picker**, and updates.

## v0.1.0
- `main-window.png` — main window: profile list with failover-group checkboxes,
  connect button, routing panel (mode, ad-block, geo direct toggles, per-app),
  failover group selector, version + updates footer.
