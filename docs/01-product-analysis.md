# Product Analysis — WeTransfer (WeTransfer-like App Reference)

**Version 0.2** | **Last updated:** 2026-08-23
**Target stack:** .NET 10 (Minimal API) · Clean (onion) architecture · CQRS via MediatR 12.5 · Azure · React 18 + TypeScript (Vite) · Visual Studio 2026
**Purpose:** developer-oriented spec of "what WeTransfer is" so an AI assistant or engineering team can build a faithful clone. Source of truth for product intent: features, limits, flows, and roles.

---

## 1. One-line definition

WeTransfer is a **brand-less, no-account-required large file transfer service**: anyone can send large files to anyone via a link, with the sender optionally requiring a password, and the recipient downloading over email with no sign-up — the link and files expire automatically.

## 2. Core product loop (the "magic")

1. Sender lands on the home page, sees one big upload surface (drag-and-drop). No account needed.
2. Sender picks files, sets an optional password, optional expiry, and (optionally) recipient email addresses.
3. A **unique transfer link** is generated (format: `{domain}/t/{8-char-id}`).
4. Sender gets a "share this link" screen with a copy button.
5. Recipient (or sender themselves) opens the link → sees file list with sizes → clicks **Download All** (a single zip) or per-file.
6. After download, the page shows a "you can send too" prompt → **the growth loop**: recipient becomes sender without an account.
7. Files auto-expire (default 7 days) and storage reclaims automatically.

**Key design properties:**

- **Zero-friction onboarding** — the #1 differentiator vs. Dropbox/Google Drive.
- **The link *is* the product surface** — recipient UX is 95% of first impressions.
- **Expiration is a feature, not a bug** — creates urgency for paid tiers (longer retention, bigger files).

## 3. Free-tier mechanics

Values are WeTransfer-typical defaults; the exact 2026 numbers must be confirmed before launch
(freeze in `Open-Decisions-and-Constants.md`, Part 2, and the Limits Registry per `03-technical-architecture.md` TA-3.4).

| Parameter | Free value (typical) |
|---|---|
| Max per-transfer size | ~5 GB |
| Max downloads per transfer | ~100 |
| Expiry | 7 days |
| Password | optional |
| Recipient email notification | yes (multiple) |
| Storage included with account | ~5 GB (if signed up) |

These numbers are the classic free-tier gate that monetizes the product.

## 4. Product suite

WeTransfer is a **platform, not a single product**. The suite:

### 4.1 WeTransfer (core transfer)

- Transfer creation, links, passwords, expiry, email
- Account: upload/download history, "My Files", resends
- Branded "From:" line (sends appear from the company, not a person)

### 4.2 WeTransfer Pro (paid tier)

- Bigger per-transfer caps (typically 20 GB / 50 GB / 100 GB tiers)
- Longer retention (e.g., 30 days)
- Custom "From" name / company branding
- Scheduling of sends
- Download analytics (who downloaded, when, how many times)
- i18n, dark mode, full mobile web

### 4.3 WeTransfer Business / Enterprise

- SSO (SAML/OIDC), SCIM provisioning
- Admin tooling: users, groups, seat management, audit logs
- Data residency options (GDPR — EU region)
- Custom subdomains (`transfers.yourcompany.com`)
- SLAs, priority support, 100 GB+ files via multipart

### 4.4 WeTransfer Collect

- **Inverted flow:** a request form. The *sender* creates a "collect" — a URL others upload **to**.
- Use cases: onboarding, briefs, auditors, real-estate closings, content agencies.
- Features: due dates, per-field validation (text + file), multi-sender folders, completion dashboard, per-file acceptance/rejection, expiration of the collection.
- This is a **different data model** (many→one, not one→many) — do not conflate with core transfer in the schema.

### 4.5 WeTransfer Sign

- E-sign: send a document for signature, collect e-signatures, audit trail, PDF merging.
- Positioned at the "documents" intersection of transfer + e-signature.

### 4.6 Albums

- **Shared galleries** for photo/video — mobile-optimized.
- Distinct from single-shot transfer: albums persist, can be updated by owner, can have multiple contributors.
- Think "shared photo drop" for clients (e.g., photographer → client).

### 4.7 Advertising (free-tier revenue)

- The classic WeTransfer model: free users see banner ads on the **recipient download page**.
- This is why their brand aesthetic (minimal, ad-adjacent) is carefully curated — ads are part of the free revenue.

## 5. User roles (build the permissions model around these)

| Role | Capabilities |
|---|---|
| **Guest sender** (no account) | Create transfer, password, expiry, email, copy link |
| **Account user (free)** | Everything above + history, My Files, storage quota |
| **Pro user** | Free + bigger caps, branding, analytics, retention |
| **Admin (enterprise)** | User/seat management, groups, audit log, SSO, data residency |
| **Recipient** (no account) | Open link, verify password, download, re-send prompt |
| **Collector** (Collect product) | Create collection, view/accept submissions |
| **Contributor** (Collect product) | Upload to a collection |
| **Signer** (Sign product) | E-sign a document |
| **Viewer** (Albums product) | View/download from an album |

## 6. Detailed feature inventory (MVP must-have)

### 6.1 Upload

- Drag-and-drop; click-to-browse
- Multi-file; **total size** validation against plan cap
- **Chunked/multipart upload** (critical: files >100 MB must not be single PUT)
- **Progress UI**: per-file and overall % (resume on failure)
- Pre-upload size check against free-tier cap

### 6.2 Transfer record

- **Unique link** (8-char URL-safe ID)
- Expiry: default 7 days, configurable by plan
- Max download count: default 100
- Optional password (bcrypt/PBKDF2-hashed)
- Sender display name ("From")
- Recipient emails (0..n) → get notification
- Optional scheduled send time

### 6.3 Recipient download page

- Account-free
- File list with per-file size + type
- **Download All** (zipped) + per-file
- Progress bar
- After download: "Send something" prompt (growth loop)
- **Ad slot** (free tier)
- Password gate screen if set
- 404/expired screen when the link is gone

### 6.4 Email notifications

- Transactional "You received a transfer" email with subject `Transfer from {sender}`
- Branded, no HTML bloat, single CTA
- Reply-to → the transfer owner's email
- Do not send email when the transfer is shared to self

### 6.5 Account

- Email/password or magic link (or SSO)
- Upload & download history (last 30 days)
- Resend old transfer (new link, fresh expiry)
- Per-plan storage metering

### 6.6 Billing (Pro)

- Stripe — monthly/annual
- Per-tier caps and retention
- Plan limits enforced **server-side** (don't trust the client)

### 6.7 Admin

- Active transfers, storage used, user list, plan assignments
- Feature flags (gate Pro features without redeploy)

## 7. Non-functional requirements (build to these)

| Concern | Target |
|---|---|
| Upload success rate | >99% |
| Link→first-byte latency | <500 ms (global) |
| Expiry job lag | <5 min past scheduled expiry |
| Email delivery | >99% within 60 s of transfer created |
| Throughput target (production) | ~10 TB/day, ~10k concurrent active transfers |
| Browser support | Chrome, Safari, Edge, Firefox (last 2 versions) + mobile web |
| i18n | English, Dutch, French, Spanish, Portuguese, Italian, German, Turkish |
| Accessibility | WCAG 2.1 AA |
| GDPR | EU data-residency option, data export, deletion |
| Security | TLS 1.2+ in transit, AES-256 at rest, per-transfer temp credentials, PBKDF2 for passwords |
| Dark mode | full parity with light mode |

## 8. Product suite — build order for a faithful clone

1. **MVP — Core transfer** (free tier): upload, link, download, email, expiry. *This is 80% of the value.*
2. **Pro tier** — billing + caps + branding. *Monetizes the core.*
3. **Account + history** — "My Files" + resends. *Improves retention.*
4. **Collect** — many→one flow, different schema. *Second product, big for agencies.*
5. **Sign** — document e-sign. *High AOV product, smaller TAM.*
6. **Albums** — shared galleries. *Mobile-first surface.*
7. **Enterprise** — SSO, SCIM, audit, residency, custom subdomain. *Highest ACV.*

## 9. Glossary (for AI context priming)

- **Transfer** — one logical send (a file set + link + recipient set + expiry)
- **Link / ID** — 8-char URL-safe identifier, global, unguessable
- **Expiration** — hard delete after N days (default 7)
- **Password** — optional gate on the recipient page
- **Branding / From** — the display name on the "From:" line (can be a company)
- **Growth loop** — the "you can send too" prompt that converts recipient→sender
- **Collect** — many-to-one upload request (different from core transfer)
- **Album** — persistent, multi-creator photo/video gallery
- **E-sign** — document signature with audit trail

## 10. Design / brand notes (what makes it "feel like WeTransfer")

- **Visual language:** minimal, whitespace-heavy, large type, one accent color (their brand is a specific blue).
- **Ad-adjacent UX:** free tier shows banner ads on the *recipient download page* — the layout must accommodate this without breaking.
- **Tone:** calm, confident, no exclamation points. "Send big files, get to work."
- **Mobile web is a first-class surface**, not a responsive afterthought.
- **The upload surface is the entire landing page** — no hero text, no features grid, no carousel. The product *is* the drag-and-drop.

## 11. What could not be fully loaded (re-verify before finalizing)

- The exact **free-tier caps** in 2026 (file size, download count, storage)
- The exact **Pro/Business per-plan pricing**
- The exact **feature split** between Pro tiers (20/50/100 GB split and retention windows)
- The **Collect** product's current feature surface (due-date enforcement, per-sender folders)
- The **Sign** and **Albums** feature details (no fully loaded source)

**Recommendation:** lock the numbers in `Open-Decisions-and-Constants.md` before freezing pricing/limits.

---

## 12. Target stack (summary)

The backend is a **Microsoft/.NET** stack; see `03-technical-architecture.md` for the full plan.

| Concern | Choice |
|---|---|
| Runtime | .NET 10, ASP.NET Core Minimal API |
| Architecture | Clean (onion): `wa.domain → wa.application → wa.infrastructure → wa.api / wa.workers` |
| Pattern | CQRS via **MediatR 12.5** (one request/handler per use case) |
| ORM / DB | EF Core 10 → Azure SQL Database |
| Object storage | Azure Blob Storage (short-lived SAS, browser-direct) |
| Queue / events | Azure Service Bus (topic `core`) |
| Background workers | Azure Functions (Flex Consumption) |
| Email | Azure Communication Hub |
| Edge | Azure Front Door (CDN + WAF) |
| Frontend | React 18 + TypeScript + Vite (SPA) |
| Dev environment | **Visual Studio 2026** + .NET 10 SDK |
