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
- **Assets:** upload from `dist/`:
  - the zip (`…win-x64.zip`) — always required;
  - its `…win-x64.zip.sha256` sidecar;
  - and, once signing is set up, its `…win-x64.zip.sig` signature (see "Update signing").
  The updater ignores a release with the zip but no sidecar at all (fails safe); once a
  signing key is embedded, the `.sig` becomes mandatory. When signing manually, run the
  sign step (above) before uploading and include the `.sig`.
- **Notes:** the release body is shown to the user in the update prompt.

## Update signing (recommended, one-time setup)

The updater verifies each update's authenticity with an embedded ECDSA P-256 public key.
A `.sha256` sidecar only proves the download wasn't corrupted; a signature proves it came
from whoever holds the private key, so a compromised GitHub release can't push a forged
update. Until a key is embedded, the updater falls back to the SHA-256 check.

**One-time key setup:**
1. Generate the key pair (requires openssl):
   ```powershell
   ./scripts/sign-release.ps1 -GenerateKey -KeyPath update-signing.key
   ```
   It prints the public key (base64). Keep `update-signing.key` secret — it is gitignored;
   never commit it.
2. Paste the printed value into `UpdateSignature.PublicKeyBase64`
   (`src/SimpleRay.Core/Update/UpdateSignature.cs`), commit, and release a new version.
   From then on the app requires a valid `.sig` on every update.
3. Add the **full private-key PEM** as the GitHub Actions secret `UPDATE_SIGNING_KEY`.
   `release.yml` then signs the portable zip and uploads `…win-x64.zip.sig` automatically.

To sign manually (fallback):
```powershell
./scripts/sign-release.ps1 -Zip dist/SimpleRay-<version>-win-x64.zip -KeyPath update-signing.key
```

> Migration: the first signed release must still carry the `.sha256` sidecar so clients on
> the pre-signing build (which only know SHA-256) can still update to it. `release.yml`
> already uploads both.

## How the update applies (for reference)
On user consent the app downloads the zip, **verifies SHA256**, extracts to a
staging dir, then launches a temp copy of itself with `--apply-update` that waits
for the app to exit, copies the new files over the install folder and relaunches.
The engine is stopped first so `sing-box.exe` isn't locked.

> Note: builds are **not code-signed** yet, so SmartScreen/AV may warn on first
> run and after updates. Add Authenticode signing of `SimpleRay.exe` (and ideally
> the zip) when a certificate is available.
