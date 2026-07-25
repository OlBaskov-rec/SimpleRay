# Releasing SimpleRay

The in-app updater looks at the GitHub **latest release** and only offers an
update when it finds a newer version with the right assets. The asset naming
below is what makes the updater work.

## Automated release (recommended)
CI builds and publishes the release for you on a version tag:

1. Bump `<Version>` in `src/SimpleRay.App/SimpleRay.App.csproj` (e.g. `0.2.0`),
   commit and push to `master`.
2. Tag and push:
   ```bash
   git tag v0.2.0
   git push origin v0.2.0
   ```
`.github/workflows/release.yml` then (on `windows-latest`) verifies the tag
matches the csproj version, fetches+verifies the runtime deps, runs tests, builds
the portable zip and the installer, and creates the GitHub release with all
assets — using the built-in `GITHUB_TOKEN` (no personal token needed). A tag with
a `-suffix` (e.g. `v0.3.0-rc1`) is published as a pre-release.

Every push/PR to `master` also runs build+test via `.github/workflows/ci.yml`.

---

## Manual release (fallback)

## 1. Bump the version
Edit `<Version>` in `src/SimpleRay.App/SimpleRay.App.csproj` (e.g. `0.2.0`).
This value is the source of truth the updater compares against the release tag.

## 2. Fetch deps (once / when pinned versions change)
```powershell
./scripts/fetch-deps.ps1
```

## 3. Build the portable package
```powershell
./scripts/publish-portable.ps1
```
Produces, in `dist/`:
- `SimpleRay-<version>-win-x64.zip`
- `SimpleRay-<version>-win-x64.zip.sha256`

## 3b. (Optional) Build the installer
Requires Inno Setup 6 (`winget install JRSoftware.InnoSetup`). Builds a per-user
installer from the portable output:
```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" /DAppVersion=<version> scripts\installer.iss
```
Produces `dist/SimpleRay-<version>-setup.exe`. It installs to
`%LocalAppData%\Programs\SimpleRay` (no admin needed; the app self-elevates for TUN,
and the in-app updater can write there). Upload it to the release as an extra asset
— the updater ignores non-`.zip` assets, so it won't interfere.

## 4. Create the GitHub release
- **Tag:** `v<version>` (e.g. `v0.2.0`) — a leading `v` is fine; pre-release
  suffixes like `-beta` are ignored by the version comparison.
- **Assets:** upload BOTH files from `dist/`. The updater requires:
  - an asset whose name ends with `win-x64.zip`
  - its sibling ending with `win-x64.zip.sha256`
  If either is missing, the updater ignores the release (fails safe).
- **Notes:** the release body is shown to the user in the update prompt.

## How the update applies (for reference)
On user consent the app downloads the zip, **verifies SHA256**, extracts to a
staging dir, then launches a temp copy of itself with `--apply-update` that waits
for the app to exit, copies the new files over the install folder and relaunches.
The engine is stopped first so `sing-box.exe` isn't locked.

> Note: builds are **not code-signed** yet, so SmartScreen/AV may warn on first
> run and after updates. Add Authenticode signing of `SimpleRay.exe` (and ideally
> the zip) when a certificate is available.
