---
name: ci-check
description: Run full local CI for QuibbleStone (API build, web type-check + build, Bicep validate) and fix issues iteratively. Use when the user says "ci check", "run ci", "validate ci", or wants to prep a branch before a PR.
disable-model-invocation: true
allowed-tools: Bash, Read, Write, Edit, Grep, Glob
---

# CI Check (QuibbleStone)

Run the local validation that mirrors `.github/workflows/ci.yml`, fix what breaks,
and report a clean status. Run from the repo root.

This is the **local arm of the feature-orchestration gate model** (Gates 1 + 2 in
`docs/FEATURE_ORCHESTRATION_PLAYBOOK.md`): a builder self-runs it on its worktree
before its branch is eligible to integrate (Gate 1), and the main session re-runs
it on the umbrella after each serial merge (Gate 2). The GitHub Actions run on the
pushed PR is Gate 3.

## Pipeline

1. **API builds:**
   ```bash
   dotnet build QuibbleStone.slnx --configuration Release
   ```
2. **Web type-checks and builds:**
   ```bash
   cd web
   npm ci        # or: npm install, if package-lock.json is absent/changed
   npm run build # tsc --noEmit + vite build
   cd ..
   ```
3. **Bicep validates** (no Azure login needed):
   ```bash
   az bicep build --file infra/main.bicep
   ```
   Treat compilation **errors** as failures. Warnings (e.g. a newer apiVersion is
   available) are acceptable - note them, do not block on them.
4. **Project sanity check** (stack-rule invariants - grep-based, no test harness needed):
   ```bash
   # FontAwesome only (no MUI icon imports):
   grep -rn "@mui/icons-material" web/src && echo "FAIL: use FontAwesome" || echo "ok: icons"
   # One SignalR connection (no HubConnectionBuilder outside the signalr hook folder):
   grep -rn "HubConnectionBuilder" web/src --include=*.ts --include=*.tsx | grep -v "web/src/signalr/" \
     && echo "FAIL: second SignalR connection" || echo "ok: one connection"
   # No hardcoded colors in components (hex / rgb outside the theme):
   grep -rnE "#[0-9a-fA-F]{3,6}\b|rgba?\(" web/src --include=*.tsx | grep -v "web/src/theme.ts" \
     && echo "WARN: hardcoded color in a component - move to theme.ts" || echo "ok: colors via theme"
   ```
   The icon and SignalR checks are **failures**; the hardcoded-color check is a
   **warning** to investigate (some matches may be legitimate SVG asset fills).
   Adjust the excluded paths if the tree grows new theme/asset locations.

> When the test harness lands (story `platform-devops/01`, Vitest + Playwright), add
> `cd web && npm run test:unit` here and keep it in sync with `ci.yml`. There is **no
> lint step** today (no ESLint configured) and **no i18n/locale check** (no i18n in
> the stack) - do not add either unless the stack actually gains them.

## Rules

- **Fix, then re-run.** Iterate until each step is clean. Do not declare success
  on a step you did not re-run after a fix.
- **Report honestly.** If something is still failing, say so with the exact output.
  Do not paper over a failure or skip a step silently.
- **Stay in scope.** Fix what breaks the build/validation; do not refactor
  unrelated code while here.
- If `dotnet build` cannot find a matching SDK, check `/global.json` against
  `dotnet --list-sdks`.

## Report format

```
CI check: PASS / FAIL
- API build:      PASS / FAIL  (notes)
- Web build:      PASS / FAIL  (notes)
- Bicep validate: PASS / FAIL  (warnings noted)
- Sanity check:   PASS / FAIL  (icons / one-connection / colors)
- Unit tests:     n/a (harness not wired yet) / PASS / FAIL
```
