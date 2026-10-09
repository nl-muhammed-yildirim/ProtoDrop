# T-049-08 — Scaffold src/wa.web (Vite 5 + React 18 + TS strict)

**Story:** US-049-03 | **Spec:** FR-049-5, AC-049-3, EC-049-3 | **Size:** M (scaffold)
**Depends on:** T-049-07

---

## Context to read (only these)

- `../../F-FND-001-solution-scaffold.md` → FR-049-5 + Technical notes (web lines only)
- `US-049-03-web-in-solution.md` → happy path + Edge cases

## Instructions

1. **Note:** at the reset baseline `src/wa.web/` may contain a leftover `node_modules/` folder only (no package.json). If so, delete that folder first — it is not part of the repo.
2. Scaffold the SPA: from `src/`, run `npm create vite@latest wa.web -- --template react-ts`.
3. Pin the stack per FR-049-5 in `package.json`: React 18, Vite **5**, TypeScript strict (`"strict": true` in tsconfig). If the template pulled a newer major (e.g. Vite 6+), downgrade to the pinned majors.
4. Set the canonical dev port explicitly: `server.port = 5173` in `vite.config.ts` (do **not** set `strictPort` — Vite may auto-increment on conflict, EC-049-3).
5. Run `npm ci`.

## Exit check

- [x] `package.json` pins React 18 + Vite 5; tsconfig has `"strict": true`
- [x] `npm run dev` serves at http://localhost:5173 (Ctrl-C after confirming)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (Node 20 LTS available).
Task T-049-08 — scaffold the React SPA.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/F-FND-001-solution-scaffold.md (FR-049-5 + Technical notes web lines) and US-049-03-web-in-solution.md (happy path + Edge cases).
Do exactly:
1. If src/wa.web exists with only a leftover node_modules/ folder (reset baseline), delete that folder first.
2. From src/ run: npm create vite@latest wa.web -- --template react-ts
3. In src/wa.web/package.json pin the stack to React 18, Vite 5 (downgrade if the template pulled newer majors), and keep TypeScript strict in tsconfig ("strict": true).
4. In src/wa.web/vite.config.ts set server.port = 5173 (do NOT set strictPort — port conflicts auto-increment per EC-049-3).
5. Run npm ci inside src/wa.web.
Done when: npm run dev serves the app at http://localhost:5173 and package.json pins React 18 + Vite 5.
Constraints: no API calls, no extra dependencies beyond the template (golden rule 1 — new packages need an ADR line).
```
