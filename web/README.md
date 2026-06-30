# web - QuibbleStone client

**React 19 + Vite + TypeScript (strict) + Material UI + FontAwesome.** Delivered
**PWA-first** (README section 4): no app-store friction, installable to a home
screen, shareable via link. The same codebase can later wrap into a native shell
(Capacitor) without a rewrite.

## Layout

```
web/
  package.json             scripts + dependencies
  tsconfig.json            single strict TS config (covers src/ + vite.config.ts)
  vite.config.ts           React plugin, dev server on :5173
  index.html               app shell
  .env.development         VITE_API_BASE_URL, VITE_SIGNALR_HUB_URL (committed, no secrets)
  src/
    main.tsx               entry: ThemeProvider + CssBaseline + <App/>
    App.tsx                placeholder page (connect, ping, show echo)
    theme.ts               MUI theme - the look-and-feel home (README section 10)
    fontawesome.ts         registers the icon set once
    signalr/useGameHub.ts  owns the one SignalR connection; exposes status + ping
    components/            presentational components (ConnectionStatus)
```

## Develop

```bash
cd web
npm install          # first time
npm run dev          # http://localhost:5173
```

The API must be running (see ../api) so the hub connection succeeds.

```bash
npm run build        # type-check (tsc --noEmit) + vite build -> web/dist
npm run typecheck    # type-check only
npm run preview      # serve the production build locally
```

## Test

The canonical harness is **Vitest** for pure logic (config: `web/vitest.config.ts`)
and **Playwright** for the browser smoke test (config at the repo root,
`../playwright.config.ts`; specs in `../tests/`). See `.claude/agents/testing-agent.md`
for strategy.

```bash
npm run test:unit    # Vitest - runs src/**/*.test.ts (the pure engine + content specs)
npm run test:e2e     # Playwright - the smoke test (loads the app, sees "Connected")
```

Notes:

- `test:unit` is the gate CI runs (`.github/workflows/ci.yml`): a failing spec
  fails the build. Pure-logic specs live next to the code they cover, named
  `*.test.ts` (for example `src/engine/template.test.ts`).
- `test:e2e` needs a **running full stack**: it boots the Vite dev server itself,
  but the smoke test asserts "Connected", so the **API hub must also be up** on
  `:5180` (start it first: `dotnet run --project ../api/QuibbleStone.Api.csproj`).
  With the API down the client stays at "Connecting..." and the smoke test fails
  by design. Playwright's Chromium is pre-provisioned in this environment - do not
  run `playwright install`.

## Conventions

- **MUI theme is where look-and-feel lives.** Pull colors/spacing from the theme;
  do not hardcode hex values or pixel spacing in components.
- **FontAwesome only** for icons, via the registered library (free packs for now).
- **One SignalR connection**, owned by a hook. New real-time features add invokes
  and handlers to it rather than opening new connections.
- **TypeScript strict stays on.** No `any`.
