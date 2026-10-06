# US-049-03 — Run the web app locally in the same solution

**Feature:** F-FND-001 — Solution & Project Scaffold | **Status:** done (T-001, 2026-08-31)

---

**Story:** As a developer working on the React SPA, I want `src/wa.web` to be part of the VS 2026 solution and runnable with plain npm commands, so that frontend work happens in one window without a second repo or a detached toolchain.
**Actor:** Developer (human or AI session).
**Goal:** `npm ci && npm run dev` from `src/wa.web` serves the app on http://localhost:5173, and `npm run build` emits `dist/`.

## Preconditions

- Node.js available locally; repo checked out.
- Solution scaffolded per F-FND-001 (T-001).

## Happy path

1. Developer opens the solution in VS 2026 — `src/wa.web` appears as a web project via its `package.json` (TA-2.5, wrapped by `wa.web.esproj`).
2. From `src/wa.web`, run `npm ci && npm run dev`.
3. Vite serves the landing stub on http://localhost:5173.
4. `npm run build` emits `dist/` with the bundle; `npm run lint` and `npm run test` (Vitest) are green.

## Alternative flows

- **CI:** the PR gate runs the same web commands (`lint`, `test:run` one-shot, `build`) — local and CI use identical tooling (F-FND-003).
- **Port conflict:** if 5173 is taken, Vite auto-increments; free the port rather than renaming it, because AGENTS.md §5.2 CORS depends on the canonical value (EC-049-3).

## Acceptance criteria

```gherkin
Given src/wa.web is a VS 2026 web project via its package.json in the solution
When I run npm ci && npm run dev from the project folder
Then Vite serves on http://localhost:5173

Given the dev server is running
When I run npm run build
Then dist/ contains the landing page bundle
And npm run lint and npm run test are green
```

## Edge cases

- Port 5173 already in use → Vite auto-increments; the canonical local port stays 5173 (EC-049-3).
- Web project is a stub at M0 — no API calls yet; the full upload surface arrives with F-TRF-001 / T-012.

## UI notes

- Minimal landing stub only at this milestone.

## Technical notes

- Vite 5 + React 18 + TypeScript strict, pinned by `package.json` (FR-049-5).
- Vitest is the web test runner: `test:run` = one-shot for CI, `test` = watch locally.
- The esproj wrapper keeps the SPA inside the solution without mixing build systems — .NET and Node builds stay separate commands.

## Links

- Feature: `F-FND-001-solution-scaffold.md` (FR-049-5, AC-049-3)
- Architecture: TA-2.5
- Milestone: T-001
