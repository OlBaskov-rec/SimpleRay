# releases/

Staging area for the artifacts published to **GitHub Releases**. `scripts/build-local.ps1`
refreshes this folder on every build with the current version's:

    SimpleRay-<version>-win-x64.zip          portable (unzip, run SimpleRay.exe)
    SimpleRay-<version>-win-x64.zip.sha256
    SimpleRay-<version>-setup.exe            per-user installer
    CHANGELOG.md                             what changed (English, then Russian)

Upload these when cutting a release — or let CI do it automatically from a pushed `v*` tag
(`.github/workflows/release.yml`). Only this README is tracked in git; the large, regenerable
binaries are ignored (see `.gitignore`).
