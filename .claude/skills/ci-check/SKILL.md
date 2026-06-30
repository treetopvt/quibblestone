---
name: ci-check
description: Run full local CI for Quibbler (API build, web type-check + build, Bicep validate) and fix issues iteratively. Use when the user says "ci check", "run ci", "validate ci", or wants to prep a branch before a PR.
disable-model-invocation: true
allowed-tools: Bash, Read, Write, Edit, Grep, Glob
---

# CI Check (Quibbler)

Run the local validation that mirrors `.github/workflows/ci.yml`, fix what breaks,
and report a clean status. Run from the repo root.

## Pipeline

1. **API builds:**
   ```bash
   dotnet build Quibbler.slnx --configuration Release
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
```
