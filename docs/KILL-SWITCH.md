# Kill-switch (WFP) — design and verification

> **Status: EXPERIMENTAL / UNVERIFIED.** The WFP layer was written and compiles, but its
> runtime behaviour (does it actually block leaks? does recovery work?) has **not** been
> verified — that needs a real machine with admin rights and a live tunnel. The feature is
> **off by default**. Do not enable it in a release until the checklist below passes.

## What it does

When enabled (Routing → "Kill-switch") and connected, it installs Windows Filtering
Platform filters that **block all outbound traffic** except:

- the **TUN interface** (`simpleray`) — so tunnelled traffic flows;
- **sing-box.exe** — so it can reach the VPN server over the physical link;
- **loopback**.

This closes the leak window between an unexpected tunnel drop and the watchdog reconnecting.

## Fail-closed semantics (chosen)

Filters live in a **non-dynamic** WFP session, so they **survive an app crash** — traffic
stays blocked (no leak) even if SimpleRay itself dies. Two backstops keep this from
bricking the machine permanently:

1. Filters are **not boot-persistent** — a reboot clears them (BFE drops non-persistent
   objects), so a reboot always restores networking.
2. `WfpKillSwitch.CleanupLeftovers()` runs on **every startup** and removes filters left by
   a previous crashed run. (Needs elevation to take effect; a normal launch elevates when
   you connect, and the elevated instance cleans up. A reboot is the fallback.)

So after a crash while engaged: relaunch SimpleRay (elevated, e.g. by connecting) **or**
reboot to restore access.

## Fail-safe

If WFP can't be opened or a filter can't be added, `Engage` throws; the app stays
connected and shows "kill-switch could not be enabled" rather than failing the connection
or leaving the machine half-blocked (the transaction is aborted).

## Verification

The step-by-step verification procedure (including the isolated `wfp-harness` used to debug
the WFP layer without the GUI) lives in
[tests/manual/kill-switch/RUNBOOK.ru.md](../tests/manual/kill-switch/RUNBOOK.ru.md).
Only after its checklist passes should the setting be considered safe to recommend.

## Files

- `src/SimpleRay.App/Engine/IKillSwitch.cs` — abstraction + no-op.
- `src/SimpleRay.App/Engine/WfpKillSwitch.cs` — WFP implementation + startup recovery.
- `src/SimpleRay.App/Engine/WfpNative.cs` — P/Invoke surface (struct layouts to validate).
- Wiring: `MainViewModel` (engage/disengage), `App.OnStartup` (`CleanupLeftovers`).
