# T-049-09 — Web scripts, landing stub, and first Vitest test

**Story:** US-049-03 | **Spec:** AC-049-3, FR-049-5/6 (web runner) | **Size:** M
**Depends on:** T-049-08

---

## Context to read (only these)

- `US-049-03-web-in-solution.md` → Acceptance criteria + Technical notes
- `../../F-FND-001-solution-scaffold.md` → AC-049-3 only

## Instructions

1. Replace the template page with a minimal landing stub (static text; no API calls — full surface arrives at T-012).
2. Add ESLint flat config (`@eslint/js` + `typescript-eslint`) and script `"lint": "eslint ."` — these dev-deps are part of the TA-2.5 web toolchain.
3. Add Vitest (dev dep) with one passing test, plus scripts: `"test": "vitest"` (watch, local) and `"test:run": "vitest run"` (one-shot for CI).
4. Confirm `"build"` emits to `dist/`.

## Exit check

- [x] `npm ci && npm run lint` → green
- [x] `npm run test:run` → 1 pass
- [x] `npm run build` → `dist/` contains the bundle

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (src/wa.web already scaffolded: Vite 5 + React 18 + TS strict).
Task T-049-09 — make the web gate green.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/US-049-03-web-in-solution/US-049-03-web-in-solution.md (Acceptance criteria + Technical notes).
Do exactly:
1. Replace src/wa.web/src/App.tsx / the template page with a minimal static landing stub (no API calls, no routing).
2. Add ESLint flat config using @eslint/js + typescript-eslint (dev deps) and add script "lint": "eslint ." to package.json.
3. Add Vitest as a dev dep with one trivially passing test in src/wa.web/src/; add scripts "test": "vitest" and "test:run": "vitest run".
4. Verify "build" outputs the bundle to dist/.
Done when: from src/wa.web, npm ci && npm run lint && npm run test:run && npm run build all pass, and dist/ contains the built bundle.
Constraints: keep the stub minimal — no state management, no API layer (those arrive with T-012).
```
