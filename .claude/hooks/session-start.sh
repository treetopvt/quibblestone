#!/bin/bash
# ----------------------------------------------------------------------------
#  session-start.sh - SessionStart hook for Claude Code on the web.
#
#  Ensures the .NET 10 SDK is available so `dotnet build` / `dotnet test` /
#  `dotnet run` work against the api/ project during a web session. The web-dev
#  container ships with Node/web tooling but NOT the .NET SDK, so without this
#  the API cannot be built, run, or tested locally (only on GitHub Actions).
#
#  Install route: Ubuntu apt (archive.ubuntu.com / security.ubuntu.com), NOT
#  Microsoft's dotnet-install.sh. The web session's egress proxy enforces an
#  org network policy that 403-blocks every Microsoft SDK CDN host
#  (builds.dotnet.microsoft.com, dotnetcli.azureedge.net, aka.ms, dot.net,
#  download.visualstudio.microsoft.com, ...), so dotnet-install.sh cannot fetch
#  anything - it was a silent Gate-3-only failure. The Ubuntu 24.04 (noble)
#  universe/main feed carries dotnet-sdk-10.0 (10.0.1xx) and IS reachable, and
#  NuGet restore against api.nuget.org works, so apt gives a complete,
#  buildable toolchain. If apt ever stops carrying .NET 10, the fix is a policy
#  change: allowlist builds.dotnet.microsoft.com (+ its CDN) for this session -
#  see docs/runbooks/web-session-dotnet-sdk.md.
#
#  Design:
#   - Remote-only: local machines already have the SDK, so this no-ops off web.
#   - Idempotent: if a .NET 10 SDK is already present (fresh install or a cached
#     container layer), it skips the ~165 MB download and only re-exports env.
#   - global.json pins .NET 10 (rollForward latestFeature, allowPrerelease), so
#     the apt SDK (10.0.1xx) satisfies the build.
#   - Synchronous (no async line): the session waits until the SDK is ready, so
#     the agent never races ahead of a half-installed toolchain. The installed
#     files persist via cached container state, so only the first session on a
#     fresh cache pays the download; later sessions hit the idempotent fast path.
#
#  Env vars written via $CLAUDE_ENV_FILE persist for the session only (the
#  installed SDK files persist via the cached container state). apt installs
#  dotnet to /usr/bin (already on the default PATH), so no PATH edit is needed.
# ----------------------------------------------------------------------------
set -euo pipefail

# Only the remote (Claude Code on the web) environment lacks the SDK.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Run apt as root; fall back to sudo if the session is not root.
if [ "$(id -u)" -eq 0 ]; then
  SUDO=""
elif command -v sudo >/dev/null 2>&1; then
  SUDO="sudo"
else
  echo "session-start: not root and no sudo - cannot apt-install the .NET SDK" >&2
  exit 0
fi

# Install the .NET 10 SDK only if one is not already present (idempotent).
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "session-start: .NET 10 SDK already present ($(dotnet --version)) - skipping install"
else
  echo "session-start: installing .NET 10 SDK via apt ..."
  export DEBIAN_FRONTEND=noninteractive
  # Refresh the package index first: the cached index can be stale relative to
  # the pool (superseded point releases -> 404 on the .deb). apt-get update
  # exits 0 even though the org policy 403s unrelated third-party PPAs (those
  # are warnings, not the Ubuntu archive we need), so it is safe under set -e.
  $SUDO apt-get update
  $SUDO apt-get install -y --no-install-recommends dotnet-sdk-10.0
  echo "session-start: .NET SDK installed:"
  dotnet --list-sdks
fi

# Session-scoped env (files persist across sessions; env vars do not). PATH is
# untouched because apt puts the dotnet host at /usr/bin.
{
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
} >> "$CLAUDE_ENV_FILE"

echo "session-start: dotnet ready ($(dotnet --version 2>/dev/null || echo 'version unknown'))"
