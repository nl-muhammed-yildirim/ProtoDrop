# ProtoDrop — Project Docs

**Name:** ProtoDrop (see `Open-Decisions-and-Constants.md`, D-01)
**Last updated:** 2026-08-31

A large file transfer platform (ProtoDrop), planned for AI-assisted development.
**Target stack:** .NET 10 (Minimal API) · Clean (onion) architecture · CQRS via MediatR 12.5 ·
Azure (SQL, Blob, Service Bus, Functions, Front Door, Communication Hub) · React 18 + TypeScript + Vite ·
**Development environment:** Visual Studio 2026.

## Documents

| Doc | Purpose |
|---|---|
| `AGENT.md` | Operating manual for the AI developer — session bootstrap, local run, test gates, autonomy boundary |
| `AGENTS.md` | Entry point for agentic tools — points to the files above in reading order |
| `PROGRESS.md` | **Rolling build state** — current task, completed list, next action (updated every session) |
| `ESCALATIONS.md` | Pending decision requests (AI → you), in the AGENT.md §8 format |
| `LOCAL-LLM-RUNBOOK.md` | How to run this project with a local LLM — context budget, prompt template, stop conditions |
| `START-HERE.md` | One page: the bootstrap chain + the exact first prompt to paste into the agent |
| `QA-SCRIPTS.md` | Manual (human) verification scripts for "manual pass in browser" exit checks |
| `01-product-analysis.md` | What WeTransfer actually is — product loop, suite, roles, NFRs, glossary |
| `02-feature-plan.md` | All features with stable IDs (`F-*`), FRs, Gherkin ACs, edge cases, limit constants, entities, event contract |
| `03-technical-architecture.md` | Components, monorepo layout, full SQL DDL, API catalog, event architecture, worker specs, security, observability, IaC, CI/CD, ADRs, feature→architecture traceability |
| `04-production-plan.md` | Environments, SLOs, on-call, cost model, DR, security/compliance, timeline, launch gate |
| `Open-Decisions-and-Constants.md` | The values that are still **TBD-you** (name, domains, prices, limits, region, brand) + frozen constants + decision log |
| `Milestone-Backlog.md` | Ordered task list (T-001…) with spec links and measurable exit checks |
| `Preflight-Checklist.md` | Human-side dependencies (Azure sub, GitHub, Stripe, DNS…) as gates |
| `UI-Reference.md` | Design contract: tokens, components, screen layouts |
| `Getting-Started-VS2026.md` | Open/build/run/test walkthrough for Visual Studio 2026 + .NET 10 |

## Features (detailed specs)

`features/` — one spec file per feature + **one file per user story**, organized as `Phase > Feature` folders.
See `features/README.md` for conventions and the full index.

## How to prompt AI with these docs

Each document contains its own prompt template in section 0 / its header.
Cite stable IDs (`F-TRF-001`, `AC-001-2`, `TA-3.2`, `T-004`) instead of paraphrasing.
The autonomy boundary for what the AI may decide alone vs. must escalate is in `AGENT.md`.
