# wa.web CHANGELOG

This file explains how Visual Studio created the project.

The following tools were used to generate this project:
- create-vite

The following steps were used to generate this project:
- Create react project with create-vite: `npm init --yes vite@latest "wa.web" -- --template=react-ts  --no-rolldown --no-immediate --eslint`.
- Updating `vite.config.ts` with port.
- Create project file (`wa.web.esproj`).
- Create `launch.json` to enable debugging.
- Add project to solution.
- Write this file.

## 2026-08-31 — aligned to project requirements (T-001 web stub)

- `wa.web.esproj`: `JavaScriptTestFramework = Vitest`, `ShouldRunBuildScript = true` (VS Build runs `package.json` build), `BuildOutputFolder = dist/`.
- `vite.config.ts`: dev/preview port **5173** (AGENT.md §5.2 — was VS-generated 54184); vitest config (jsdom, `src/**/*.{test,spec}.{ts,tsx}`, globals).
- `.vscode/launch.json`: browser launch URLs 54184 → 5173; `edge` → `msedge`.
- `tsconfig.app.json`: `strict: true` (TA-8.1), `DOM.Iterable` lib, `vitest/globals` types, `@/*` → `./src/*` path alias (matches `@` alias in `vite.config.ts`).
- `package.json`: added `test: vitest`, `test:run: vitest run`; dev deps `vitest`, `jsdom`, `@testing-library/react`.
- Replaced Vite scaffold with ProtoDrop landing (UI-Reference.md): `styles/tokens.css` (design tokens, light/dark), `features/landing/DropZone.tsx` (drop zone §4.1: `role=button`, `aria-label`, drag-over state, Choose files button), `App.tsx` top bar with "ProtoDrop" wordmark + Sign in; removed `assets/`, `App.css`, `index.css`.
- `index.html`: title "ProtoDrop", meta description, favicon kept.
- Smoke test `src/App.test.tsx` (vitest + testing-library): renders brand + drop zone.
- Gate green: `npm run lint` ✓, `npm run test:run` ✓ (1/1), `npm run build` ✓ (JS 60.5 kB gz — TA-8.4 budget).
