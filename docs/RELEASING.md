# Releasing SimpleRay

The in-app updater looks at the GitHub **latest release** and only offers an
update when it finds a newer version with the right assets. Follow this exactly
so the updater works.

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
