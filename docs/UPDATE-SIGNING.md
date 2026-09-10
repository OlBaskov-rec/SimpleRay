# Update signing

SimpleRay's in-app updater accepts a new version only if it can prove **where the build
came from**. This protects the *update channel* and is separate from Authenticode /
SmartScreen code-signing of the exe (see [RELEASING.md](RELEASING.md)).

## Why it matters

The updater downloads the portable zip from the GitHub *latest release* and verifies it
before applying. There are two levels:

- **SHA-256 sidecar** (`…win-x64.zip.sha256`) — proves the download wasn't *corrupted*. It
  does **not** prove authenticity: anyone who can publish or replace a GitHub release (a
  leaked token, a malicious maintainer, an account takeover) can swap the zip **and** its
  `.sha256`. A forged update then runs with the app's privileges (TUN, WFP, self-elevation).
- **ECDSA P-256 signature** (`…win-x64.zip.sig`) — proves the build was signed by whoever
  holds the private key. A compromised release **cannot** forge it.

Until a signing public key is embedded, the updater falls back to the SHA-256 check
(`UpdateSignature.PublicKeyBase64` is empty → integrity only). Embedding the key makes a
valid signature **mandatory** on every update.

## Prerequisite

**openssl** on PATH — `winget install ShiningLight.OpenSSL`, or use the openssl bundled with
Git for Windows.

## One-time setup

### 1. Generate the key pair

```bash
powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 -GenerateKey -KeyPath update-signing.key
```

`-ExecutionPolicy Bypass` avoids the *"running scripts is disabled on this system"* error on
a machine with a restrictive policy; it applies only to this one invocation and changes
nothing permanently.

This writes:

- `update-signing.key` — the **private key** (EC P-256, PEM). **Secret.** Already gitignored
  (`*.key`, `update-signing.*`); never commit it.
- …and prints the **public key** (base64, SubjectPublicKeyInfo / DER) to the console.

### 2. Embed the public key in the app

Paste the printed base64 into `src/SimpleRay.Core/Update/UpdateSignature.cs`:

```csharp
public const string PublicKeyBase64 = "<paste the printed base64 here>";
```

Commit and release a new version. From this release on, every client **requires** a valid
`.sig` — an update without one is rejected.

### 3. Give CI the private key

Add the **full private-key PEM** (the entire contents of `update-signing.key`) as the GitHub
Actions secret **`UPDATE_SIGNING_KEY`** (repo → *Settings → Secrets and variables → Actions*).
[`release.yml`](../.github/workflows/release.yml) then signs the zip and uploads
`…win-x64.zip.sig` on every tagged release, automatically.

## Signing a release

- **CI (automatic):** once `UPDATE_SIGNING_KEY` is set, pushing a `v*` tag signs and uploads
  the `.sig` with no extra steps.
- **Manual (fallback):**
  ```bash
  powershell -ExecutionPolicy Bypass -File .\scripts\sign-release.ps1 -Zip .\releases\SimpleRay-<version>-win-x64.zip -KeyPath update-signing.key
  ```
  Produces `…win-x64.zip.sig` next to the zip; upload it with the release.

## Storing the private key (important)

The private key is the **root of trust** for updates: anyone who holds it can push code to
every user. Treat it like a production secret.

- **Do** keep it as the GitHub secret `UPDATE_SIGNING_KEY` for CI **and** keep one **offline
  backup** in a password manager or encrypted vault, stored outside the repo tree.
- **Don't** commit it, paste it into chat / notes / cloud docs in plaintext, or email it. The
  `.gitignore` rules for `*.key` / `update-signing.*` are only a safety net for this working
  tree — they don't protect a copy you put elsewhere.
- **If you lose it** you can still ship updates signed by a *new* key, but only to installs
  that already embed the new public key; older installs will reject them and must be updated
  manually. So back it up.

## Rotating / responding to compromise

1. Generate a new key pair (step 1).
2. Ship a transition release **signed with the OLD key** whose app embeds the **NEW** public
   key. Existing clients accept it (the old signature still verifies) and pick up the new key.
3. From the next release, sign with the NEW key and update the `UPDATE_SIGNING_KEY` secret.
4. Retire the old key.

You cannot skip the transition: a release signed with a brand-new key is rejected by clients
that still trust only the old public key.

## Migration note

The **first** signed release must still carry the `.sha256` sidecar, so clients on a
pre-signing build (which only know SHA-256) can update to it. `release.yml` uploads both, so
this is automatic.

## Related

- [RELEASING.md](RELEASING.md) — the full release process.
- [SECURITY-AUDIT.md](SECURITY-AUDIT.md) — the update trust-chain audit.
- Authenticode / SmartScreen code-signing of `SimpleRay.exe` is a **separate** concern (needs
  a code-signing certificate) and is not covered here.
