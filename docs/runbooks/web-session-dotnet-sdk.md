<!--
  Runbook: make the .NET 10 SDK available in Claude Code on the web sessions.

  Web sessions run in an egress-policy-restricted container: the SDK is NOT
  pre-installed and Microsoft's own SDK download hosts are 403-blocked by the org
  network policy. This runbook records the diagnosis, the automated fix (apt), and
  the exact allowlist a policy owner would change if apt ever stops working. It
  exists because a blocked SDK download was a silent Gate-3-only failure - the API
  could not be built or tested in-session, only on GitHub Actions.

  Prose uses hyphens, colons, parentheses - never em dashes.
-->

# Runbook: .NET SDK in web sessions

## TL;DR

The `.claude/hooks/session-start.sh` SessionStart hook installs the .NET 10 SDK
from the **Ubuntu apt archive** on every fresh web-session container. This works
today with no action needed - `dotnet build QuibbleStone.slnx` and `dotnet test`
run in-session. This runbook is for when that route breaks.

## Diagnosis (what was wrong)

The old hook fetched Microsoft's `dotnet-install.sh` and downloaded the SDK from
Microsoft's CDN. In a web session the outbound HTTPS proxy enforces the org
network policy, which **403-blocks every Microsoft SDK host**. Confirmed blocked
(via `curl -sS "$HTTPS_PROXY/__agentproxy/status"` and direct probes):

| Host | Result |
|---|---|
| `builds.dotnet.microsoft.com` | 403 (CONNECT rejected - policy denial) |
| `dotnetcli.azureedge.net` | 403 |
| `dotnetcli.blob.core.windows.net` | 403 |
| `dotnetbuilds.azureedge.net` | 403 |
| `download.visualstudio.microsoft.com` | 403 |
| `aka.ms` | 403 |
| `dot.net` / `dotnet.microsoft.com` | 301/302 (redirect host allowed, but it only forwards to the blocked CDNs) |

The proxy README (`/root/.ccr/README.md`) is explicit: a 403/407 is an org policy
denial - **do not retry or route around it, report it**. So `dotnet-install.sh` is
fundamentally unusable in a web session until the policy changes.

## The fix (automated - already in place)

The Ubuntu 24.04 (noble) archive **is** reachable and carries .NET 10:

- `archive.ubuntu.com` / `security.ubuntu.com` - serve `dotnet-sdk-10.0`
  (currently `10.0.1xx`) from `noble-updates` main/universe.
- `api.nuget.org` - NuGet restore works, so packages resolve.

So `session-start.sh` now runs (remote-only, idempotent):

```bash
apt-get update                                        # refresh stale index (avoids 404 on superseded .debs)
apt-get install -y --no-install-recommends dotnet-sdk-10.0
```

apt installs the host to `/usr/bin/dotnet` (already on PATH). `global.json`
(pinned `10.0.100`, `rollForward: latestFeature`, `allowPrerelease`) is satisfied
by the apt SDK. Verified in-session: `dotnet build QuibbleStone.slnx` succeeds and
`dotnet test` passes all tests.

Note: `apt-get update` prints 403 warnings for unrelated third-party PPAs
(deadsnakes, ondrej/php) that the policy also blocks. Those are harmless - they
are not the Ubuntu archive we need, and `apt-get update` still exits 0.

## If apt ever stops carrying .NET 10 (escalation - owner action)

If the Ubuntu archive drops or lags .NET 10, the only fix is a **network policy
change** by whoever owns the environment. Two options:

1. **Allowlist the Microsoft SDK hosts** for web sessions, then revert the hook to
   `dotnet-install.sh --channel 10.0`. Minimum hosts to allow:
   - `builds.dotnet.microsoft.com` (SDK binaries - the primary one)
   - `dotnetcli.azureedge.net` and `dotnetcli.blob.core.windows.net` (CDN/blob fallback)
   - `aka.ms` and `dot.net` (redirect/bootstrap for `dotnet-install.sh`)
   - `api.nuget.org` (already reachable - keep it)

2. **Pick a less restrictive network policy** when creating the environment, per
   https://code.claude.com/docs/en/claude-code-on-the-web (the network-policy
   section governs which outbound hosts a session may reach).

Do not disable TLS verification or unset `HTTPS_PROXY` - see the proxy README.

## CI is unaffected

`.github/workflows/ci.yml` uses `actions/setup-dotnet@v4` on GitHub-hosted
runners with full egress. This runbook is only about the in-session web container.

## Quick re-diagnosis in a session

```bash
curl -sS "$HTTPS_PROXY/__agentproxy/status"          # recentRelayFailures lists blocked hosts
dotnet --version || bash .claude/hooks/session-start.sh   # (re)install
dotnet build QuibbleStone.slnx && dotnet test QuibbleStone.slnx
```
