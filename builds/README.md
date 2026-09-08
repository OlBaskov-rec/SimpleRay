# builds/

Local archive of packaged SimpleRay builds. `scripts/build-local.ps1` drops each
built version here under its own subfolder:

    builds/<version>/SimpleRay-<version>-win-x64.zip
    builds/<version>/SimpleRay-<version>-win-x64.zip.sha256
    builds/<version>/SimpleRay-<version>-setup.exe   (if Inno Setup is installed)

Unlike `build/` (the latest-only pickup folder), this folder **accumulates**
every version, so past portable/installer builds stay available locally for
comparison, rollback testing or manual distribution.

The binaries themselves are **not** committed to git — they are large and fully
regenerable from a tagged source revision. Only this README is tracked (see the
`/builds/*` + `!/builds/README.md` rule in `.gitignore`). For a version that is
actually distributed, publish it through CI by pushing a `v*` tag.
