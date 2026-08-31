# wa.web — ProtoDrop frontend

React + TypeScript + Vite single-page app. Served in production as static assets (Azure Static Web Apps, TA-7.4); locally via the Vite dev server on **port 5173** (`npm run dev`).

Open in Visual Studio 2026 as `wa.web.esproj` (JavaScript/TypeScript project — build runs `package.json` scripts).

## Commands

| Command | Purpose |
|---|---|
| `npm run dev` | dev server → http://localhost:5173 |
| `npm run build` | `tsc -b` typecheck + `vite build` → `dist/` |
| `npm run test:run` | run vitest suite once (CI gate; use `npm run test` for watch) |
| `npm run lint` | eslint |

Test gate per `AGENT.md` §4: `npm run lint && npm run test:run && npm run build`.

## Structure (TA-8.2)

```
src/
├─ main.tsx  App.tsx
├─ styles/        # tokens.css (UI-Reference.md design tokens)
├─ core/          # api/, upload/, auth/, i18n/, theme/  (added with T-004+)
└─ features/      # landing/ (drop zone — F-TRF-001), sender/, recipient/, …
```

Design tokens: `src/styles/tokens.css` is the on-disk mirror of `docs/UI-Reference.md` §1–3 — keep them in sync.

## Tooling

`wa.web.esproj` (Microsoft.VisualStudio.JavaScript.Sdk): `StartupCommand = npm run dev`, `JavaScriptTestFramework = Vitest`, `ShouldRunBuildScript = true` (VS **Build** runs `package.json` build → `dist/`), `BuildOutputFolder = dist/`.

Dev tooling: `vitest` + `jsdom` + `@testing-library/react` (smoke test: `src/App.test.tsx`).
