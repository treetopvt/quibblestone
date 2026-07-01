#!/bin/bash
# ----------------------------------------------------------------------------
#  session-start.sh - SessionStart hook for Claude Code on the web.
#
#  Ensures the .NET 10 SDK is available so `dotnet build` / `dotnet test` /
#  `dotnet run` work against the api/ project during a web session. The web-dev
#  container ships with Node/web tooling but NOT the .NET SDK, so without this
#  the API cannot be built, run, or tested locally (only on GitHub Actions).
#
#  Design:
#   - Remote-only: local machines already have the SDK, so this no-ops off web.
#   - Idempotent: if a .NET 10 SDK is already present (fresh install or a cached
#     container layer), it skips the ~235 MB download and only re-exports PATH.
#   - global.json pins .NET 10 (rollForward latestFeature, allowPrerelease), so
#     the 10.0 channel's latest SDK satisfies the build.
#   - Synchronous (no async line): the session waits until the SDK is ready, so
#     the agent never races ahead of a half-installed toolchain. The container
#     state is cached after the hook completes, so only the first session on a
#     fresh cache pays the download; later sessions hit the idempotent fast path.
#
#  Env vars written via $CLAUDE_ENV_FILE persist for the session only (the
#  installed SDK files persist via the cached container state).
# ----------------------------------------------------------------------------
set -euo pipefail

# Only the remote (Claude Code on the web) environment lacks the SDK.
if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

DOTNET_DIR="$HOME/.dotnet"

# Install the .NET 10 SDK only if one is not already present (idempotent).
if [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "session-start: .NET 10 SDK already present at $DOTNET_DIR - skipping install"
else
  echo "session-start: installing .NET 10 SDK to $DOTNET_DIR ..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  # --channel 10.0 picks the latest 10.0.x; --no-path because we export it below.
  /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR" --no-path
  echo "session-start: .NET SDK installed:"
  "$DOTNET_DIR/dotnet" --list-sdks
fi

# Put dotnet on PATH for the whole session (env-file writes run every session,
# since env vars - unlike the installed files - do not persist across sessions).
{
  echo "export DOTNET_ROOT=\"$DOTNET_DIR\""
  echo "export PATH=\"$DOTNET_DIR:\$PATH\""
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
} >> "$CLAUDE_ENV_FILE"

echo "session-start: dotnet ready ($("$DOTNET_DIR/dotnet" --version 2>/dev/null || echo 'version unknown'))"
