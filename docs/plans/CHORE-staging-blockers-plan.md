# CHORE-staging-blockers — plan

**Branch:** `fix/CHORE-staging-blockers` (from origin/develop @ 415fc13, includes #711 IaC + #712 per-call DbContext)
**Authority:** owner, 2026-11-26 ("fix them all") — pre-prod blockers found while planning T-028.
**Specs:** FR-008-1/3, AC-008-1, EC-008-1/4 (F-TRF-008 / US-008-01), TA-9.1 (AuthN), TA-4.1.3 (closed ProblemCodes), TA-10.2 (closed telemetry), TA-3.6 (SAS strategy), TA-8.2 (web structure).

## 1. Goal

Close two defects that block any deployed environment: (1) `POST /api/v1/auth/signup` on an existing email mints a session **without checking the password** (account takeover); (2) the SPA re-signs every minted blob SAS URL with the public Azurite dev key, so real Azure Blob answers 403. **Exit check:** signup on an existing email with a wrong password, a password-less (magic-link-only) account or a soft-deleted account → 401 `WRONG_PASSWORD` + `login_failed`, no token; a correct password → 200 with the same body as today. A host-style `https://{acct}.blob.core.windows.net/...` SAS goes from the server to `fetch`/`location` unchanged, byte for byte. Azurite URLs are still re-signed exactly as today. Full local gate is green; integration runs in CI.

## 2. Design & Decisions

### D-1 Signup on a known email = login (password verified)
- Today, `SignupHandler.cs:48-72`: a known email goes to `user = existing` and gets `_tokens.Mint(user.Id)` at `:82` with no password check. The token reaches the client **only in the JSON body** (`sessionToken`, `AuthEndpoints.cs:29-37`). Signup sets no cookie, unlike login at `AuthEndpoints.cs:125-131`. The SPA (`AuthPage.tsx:85-104`) and e2e (`e2e/mobile-helpers.ts:141-147`) install that body token as `wa.session` themselves, so the leaked token is a full session.
- Fix: the known-email branch runs the **same credential check as `LoginHandler`** (TA-9.1: `PasswordHasher<string>` PBKDF2-SHA256, keyed by `user.Id.ToString("n")`, `LoginHandler.cs:47-56`). On failure it logs `TelemetryEvents.LoginFailed` and throws the existing `LoginWrongPasswordException.ForWrongPassword()`. The signup endpoint maps that to `BusinessException(ProblemCodes.WrongPassword, …)`, the same mapping login uses at `AuthEndpoints.cs:145-149`. So the response is 401 `WRONG_PASSWORD`, byte-identical to a failed login. **No new ProblemCode** (TA-4.1.3) and **no new telemetry name** (TA-10.2; `login_failed` is at `TelemetryEvents.cs:42`).
- Null `PasswordHash` (magic-link-only account) counts as a failed verify. This applies the 2026-11-25 decision (Open-Decisions Part 3 row "CHORE-code-style-2 login with no password hash") to signup as well. Before, such an account could be taken over through signup.
- On success, the result is the user's **own plan**: `IPlansStore.GetByIdAsync(user.PlanId)`, as at `LoginHandler.cs:59-61`. Today signup returns `freePlan.Code` for every existing user (`:81`), which is wrong for a Pro user. A missing plan row gives the same `WrongPassword` shape as login. Only `login_success` is emitted (no `user_created`). The token comes from the same `_tokens.Mint(user.Id)` call. The create branch does not change (`user_created` + `login_success`, Free plan, PBKDF2 hash).
- **Shared check, no duplication (codebase-design, deep module):** a new `internal static class PasswordSignIn` in `wa.application/UseCases/Auth/` with one method, `Accepts(AppUser user, string password)`. It returns false when the user is soft-deleted, has a null hash, or fails verification. `LoginHandler` and `SignupHandler` both call it, so the two auth paths cannot drift again. Rule 10 (one type per file): the new type gets its own file. It is internal and static: application-layer business logic with no port and no DI (onion rule; TA-2.x layering is unchanged).

### D-2 Soft-deleted accounts are never signed into (signup and login)
- `AppUserStore.FindByEmailAsync` (`AppUserStore.cs:17-21`) does **not** filter `DeletedAtUtc`, while `GetByIdAsync` does (`:36`). So login currently signs a soft-deleted account in when the password is right. Every session-gated read then 404s via `GetByIdAsync`, but `/send` would attribute transfers to a deleted id. Signup would do the same with any password.
- Decision: `PasswordSignIn.Accepts` returns false when `DeletedAtUtc` is not null. Both handlers therefore treat a deleted account like an unknown or wrong-password login: 401 `WRONG_PASSWORD` + `login_failed`. That is the existing no-leak shape and needs no new code. The guard sits in the handler, not the store query. That keeps `FindByEmailAsync` able to see the row, so signup never tries to INSERT a second row for the same email, which would hit `UQ_AppUser_Email` and return a 500.
- The login change is one line. It is in scope because "a deleted account must not be signed into" covers login as well (owner brief). It gets one new `LoginHandlerTests` case.

### D-3 Account-existence signal: accepted, matches login
After the fix, signup with a known email and a wrong password returns 401 `WRONG_PASSWORD`, while an unknown email returns 200 and creates an account. A caller can therefore tell that an email exists. This is accepted. It is strictly better than today: today the same probe returns a **session** for the account. Login itself returns `WRONG_PASSWORD` for both "no row" and "bad hash", so login stays non-enumerating, but signup cannot be: it must create unknown emails (FR-008-3). Closing the signal fully would need an email-confirmation-first signup flow, which is out of scope (see Open Questions, Q-1). It needs no `DECISION:` block: the brief tells us to match login and note it.

### D-4 One shared, gated Azurite SAS helper (`core/blob/azuriteSas.ts`)
- Today, `StagingList.tsx:36-86` (`reSignForHttp` / `reSignUploadUrl`, called at `:166`) and `RecipientStore.ts:43-87` (`browserDownloadUrl`, called at `:425` download-all/zip and `:482` per-file) **always** do two things:
  1. move the minted URL to `/devstoreaccount1{path}`;
  2. re-sign it with the hard-coded Azurite key (`spr=https,http`).
  
  The Open-Decisions Part 3 row dated 2026-10-20 ("T-003-08 recipient download — client-side re-address …") assumed production URLs would be left alone. The code does not do that. `BlobSasMinter.Mint` (`BlobSasMinter.cs:190`) emits `{accountUrl authority}/{container}/{blob}?…` with `spr=https`. On real Azure the re-signed signature is wrong and the account path segment is invalid, so the result is a 403.
- New module `src/wa.web/src/core/blob/azuriteSas.ts` (TA-8.2 `core/` = cross-feature infrastructure; the "features are self-contained" note in `RecipientStore.ts:46` was about duplicating constants, which this removes). Its exports:
  - AZURITE_LOOPBACK_ORIGIN = 'http://127.0.0.1:10000', the fixed origin RecipientStore uses today (`:47`).
  - `isAzuriteBlobUrl(url: string): boolean`. It is true when the URL's `hostname` is one of `127.0.0.1`, `localhost`, host.docker.internal or `[::1]` **and** its `port === '10000'`, **or** its `pathname` starts with `/devstoreaccount1/`. Every other URL is false, including `*.blob.core.windows.net`, any https host on the default port, and the test fixture host azurite.example.com. A URL that fails to parse is also false, so the caller uses it verbatim.
  - `toBrowserBlobUrl(mintedUrl: string, origin?: string): Promise<string>`. When the URL is not Azurite it returns `mintedUrl` **unchanged**: the same string reference, no `URL` round-trip, so the bytes are identical. When it is Azurite, it rebuilds the URL with today's algorithm:
    - It reads `sp` (default `'r'`), `st`, `se`, `sv` (default `'2024'`), `sr` (default `'b'`) and `rscd` from the query.
    - It builds the 16-line blob-SAS string-to-sign: `sp, st, se, /blob/devstoreaccount1{pathname}, '', '', 'https,http', sv, sr, '', '', '', rscd, '', '', ''`.
    - It signs that with HMAC-SHA256 under the Azurite key (crypto.subtle).
    - It emits `${origin ?? minted.origin}/devstoreaccount1${pathname}?sv=${sv}&spr=https%2Chttp&st=${enc(st)}&se=${enc(se)}&sr=b&sp=${sp}` + (`rscd` non-empty ? `&rscd=${enc(rscd)}` : '') + `&sig=${enc(sig)}`.
- **Parity with today:**
  - **Upload SAS** (minted `sp=cwr`, `sv=2024`, no `rscd`): the string-to-sign lines and the output equal `StagingList.reSignUploadUrl` exactly. StagingList keeps its minted origin, so it passes no `origin`.
  - **Recipient SAS** (`rscd` always signed: the file disposition, or all.zip for the zip at `BlobSasMinter.cs:111`): the lines and output equal `browserDownloadUrl` exactly. RecipientStore passes AZURITE_LOOPBACK_ORIGIN.
  - The only change is an empty `rscd`: the output now omits `&rscd=` instead of emitting it empty. The signed value is the same (`''`), and the server never mints that for recipients.
- Not touched, already correct: the org logo (`OrgLogoSection.tsx:132`) and data export (`DataExportSection.tsx:112`) use the minted URL verbatim, which is right for production. Neither is part of this defect. The e2e-only re-signers (`e2e/seed-transfer.ts:85`, `e2e/global-setup.ts`) are test tooling against Azurite and stay as they are.
- The Azurite key string still ships in the bundle, now once instead of twice. It is Microsoft's public, well-known emulator key, not a secret, and it can no longer touch a non-Azurite URL. Gating it on `import.meta.env.DEV` was rejected: local Playwright may run a built bundle, and the runtime URL check is the actual safety property.

### D-5 Merge overlap with open PR #713 (`fix/CHORE-deploy-hardening`)
PR #713 changes `StagingList.tsx:3` (adds the API_BASE import after line 2) and `:33-36` (DRAFT_ENDPOINT/FINALIZE_ENDPOINT). It also changes `RecipientStore.ts:2` (import) and `:29-36` (endpoint constants).
- **StagingList:** this plan deletes `:36-37` and the block down to `:86`. That is next to #713's `:35` hunk, so **a textual conflict is expected**. Resolution: keep #713's API_BASE lines and drop the two AZURITE_* lines.
- **Imports:** the new import goes as the **last** import line, after `import { useFinalize } from './finalizeState'` (`StagingList.tsx:15`) and after `import { trackClientEvent } …` (`RecipientStore.ts:3`). That keeps it clear of #713's line-2/3 insertion.
- **Endpoint constants:** none are touched here.

## 3. Implementation Plan

1. **Shared credential check.** CREATE `src/wa.application/UseCases/Auth/PasswordSignIn.cs`:
   ```csharp
   using Microsoft.AspNetCore.Identity;
   using wa.domain;

   namespace wa.application.UseCases.Auth;

   /// <summary>TA-9.1: the one password sign-in check shared by login and signup-on-a-known-email (CHORE-staging-blockers) ...</summary>
   internal static class PasswordSignIn
   {
       /// <summary>True only for a live account whose stored PBKDF2 hash verifies the given password ...</summary>
       /// <param name="user">Account found by email.</param>
       /// <param name="password">Plain-text password from the request.</param>
       /// <returns><see langword="true"/> when the password verifies; false for a soft-deleted account, a magic-link-only account (no hash) or a wrong password.</returns>
       internal static bool Accepts(AppUser user, string password)
       {
           if (user.DeletedAtUtc is not null || user.PasswordHash is null)
               return false;
           var result = new PasswordHasher<string>().VerifyHashedPassword(user.Id.ToString("n"), user.PasswordHash, password);
           return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
       }
   }
   ```
   XML docs are required (CS1591 is a build error). Write meaningful summaries; the "..." above is a placeholder.
2. **LoginHandler.** MODIFY `src/wa.application/UseCases/Auth/LoginHandler.cs:47-56`: replace the inline hasher and `if` with `if (!PasswordSignIn.Accepts(user, request.Password)) { login_failed; throw ForWrongPassword(); }` and keep the comments. Remove `using Microsoft.AspNetCore.Identity;` if it is now unused (IDE0005 with TreatWarningsAsErrors). Add one sentence to the class `<summary>` saying a soft-deleted account fails the same way.
3. **SignupHandler.** MODIFY `src/wa.application/UseCases/Auth/SignupHandler.cs:36-83`:
   - After validation: `var existing = await _users.FindByEmailAsync(email, ct); if (existing is not null) return await SignInExistingAsync(existing, request.Password, ct);`.
   - Then the unchanged create branch, with `freePlan` now resolved inside it, plus `login_success`, and `new SignupResult(..., freePlan.Code, _tokens.Mint(user.Id))`.
   - New `private async Task<SignupResult> SignInExistingAsync(AppUser user, string password, CancellationToken ct)`, written exactly like LoginHandler:
     - `if (!PasswordSignIn.Accepts(user, password)) { _telemetry.LogEvent(TelemetryEvents.LoginFailed); throw LoginWrongPasswordException.ForWrongPassword(); }`
     - `var plan = await _plans.GetByIdAsync(user.PlanId, ct) ?? throw LoginWrongPasswordException.ForWrongPassword();`
     - `_telemetry.LogEvent(TelemetryEvents.LoginSuccess);`
     - `return new SignupResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, _tokens.Mint(user.Id));`
   - The method needs a `<summary>`, `<param>` tags, `<returns>` and `<exception cref="LoginWrongPasswordException">`.
   - Rewrite the class `<summary>` (`:12-16`): "known email → verified like login (wrong/absent password or deleted account → WRONG_PASSWORD), never a password-less sign-in". Fix the EC-008-4 comment at `:47`.
4. **Endpoint mapping.** MODIFY `src/wa.api/Endpoints/AuthEndpoints.cs:24-45`: add `catch (LoginWrongPasswordException ex) { throw new BusinessException(ProblemCodes.WrongPassword, ex.Message, ex); }` next to the existing `SignupValidationException` catch, copying login's `:145-149`. Update the route comment at `:23` ("known email → password-verified sign-in"). Do **not** add a cookie (see Q-2). The 200 body does not change.
5. **Unit tests.** MODIFY `tests/wa.application.unit/Auth/SignupHandlerTests.cs`:
   - Fix `Signup_KnownEmail_DifferentCase_FindsSameRow_NoDuplicate` (`:84-109`). Its `PasswordHash = "stored-hash"` would now fail verification, so seed a real hash: `new PasswordHasher<string>().HashPassword(existingId.ToString("n"), "password123")`.
   - Add a helper `SeedExisting(Guid id, string? passwordHash, DateTime? deletedAtUtc = null)`.
   - New facts (EC-008-4 + AC-008-1 security half):
     - `Signup_KnownEmail_CorrectPassword_ReturnsToken_EmitsLoginSuccessOnly`: token is non-empty, `UserId == existingId`, no `user_created`, exactly one `login_success`, `Created` is empty.
     - `Signup_KnownEmail_WrongPassword_ThrowsWrongPassword_NoToken_EmitsLoginFailed`: `Assert.ThrowsAsync<LoginWrongPasswordException>`, exactly one `login_failed`, zero `login_success`, `Created` is empty.
     - `Signup_KnownEmail_MagicLinkOnlyAccount_ThrowsWrongPassword`: `PasswordHash = null`, same asserts as the wrong-password fact.
     - `Signup_KnownEmail_SoftDeleted_ThrowsWrongPassword_EvenWithCorrectPassword`: real hash plus `DeletedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`, same asserts, no insert.
     - `Signup_KnownEmail_ReturnsUsersOwnPlanCode`: the stub plan has the user's `PlanId`, so assert `PlanCode == _freePlan.Code` via `GetByIdAsync`. Keep it minimal: the stub has one plan. That this is the user's own plan, not the default one, is pinned by an assertion that `GetAsync` was **not** needed. Add `public int GetAsyncCalls` to `StubPlansStore` and assert 0 on the known-email path.
   - MODIFY `tests/wa.application.unit/Auth/LoginHandlerTests.cs`: add `Login_SoftDeletedAccount_ThrowsWrongPassword_EmitsLoginFailed`, using `SeedRealAppUser` (`:38-49`) with `DeletedAtUtc` set and the correct password. Existing facts stay green unchanged.
   - Verify: dotnet test tests/wa.application.unit with filter FullyQualifiedName~Auth, then the full `dotnet test tests/wa.application.unit`.
6. **Integration test (CI-only; Docker is down locally).** MODIFY `tests/wa.api.integration/SignupEndpointTests.cs`. The existing three facts stay valid: the different-case fact reuses the same password. Add:
   - `Signup_ExistingEmail_WrongPassword_Returns401_WRONG_PASSWORD_NoSessionToken`: first signup returns 200; second signup with the same email and `"different-pass-1"` returns 401, `code == "WRONG_PASSWORD"`, `status == 401`, the body has no `sessionToken` property, and the row count stays 1.
   - `Signup_SoftDeletedAccount_Returns401_WRONG_PASSWORD`: signup returns 200, then `_db.Set<AppUser>().Where(u => u.Email == E).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAtUtc, (DateTime?)new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))`, then a second signup with the **same** password returns 401 `WRONG_PASSWORD`.
   
   Other routes checked for stale facts: `grep "auth/signup" tests/wa.api.integration` gives only `SignupEndpointTests.cs` and `LoginReflectsPlanChangeTests.cs:62` (a single signup with a fresh email, unaffected). `e2e/mobile-helpers.ts:141` uses a random email, unaffected.
   
   Verify locally: `dotnet build tests/wa.api.integration`.
7. **Azurite helper.** CREATE `src/wa.web/src/core/blob/azuriteSas.ts` as specified in D-4, with a TSDoc block citing TA-3.6, TA-14.3/T-055-05 and this CHORE. The key constant moves here verbatim from `StagingList.tsx:36`. Helpers stay module-private: `toBase64` and key import, with the import done once per call as today.
8. **Helper tests.** CREATE `src/wa.web/src/core/blob/azuriteSas.test.ts` (vitest; jsdom/node crypto.subtle is available, see `RecipientPage.test.tsx:956`):
   - `isAzuriteBlobUrl` truth table. True: `http://127.0.0.1:10000/staging/x`, `http://localhost:10000/…`, http://host.docker.internal:10000/…, `http://example.test:9999/devstoreaccount1/x`. False: `https://acct.blob.core.windows.net/transfers/x?sig=a`, https://azurite.example.com/transfers/x, `http://127.0.0.1:8080/x`, `not a url`.
   - **Production verbatim:** for `https://wastg01.blob.core.windows.net/transfers/abc/def?sv=2024&spr=https&se=2030-01-01T00%3A30%3A00Z&sr=b&sp=r&rscd=attachment%3Bfilename%2A%3DUTF-8%27%27a.txt&sig=Zm9v%2Bbar%3D`, `await toBrowserBlobUrl(u)` must `toBe(u)`. Test it once with an `origin` argument and once without.
   - **Azurite upload parity:** a minted `http://127.0.0.1:10000/staging/d/f?sv=2024&spr=https&st=2026-01-01T00%3A00%3A00Z&se=2026-01-01T02%3A00%3A00Z&sr=b&sp=cwr&sig=x` is compared with an **oracle**. The oracle is a verbatim copy, inside the test, of today's `reSignUploadUrl` (`StagingList.tsx:73-86`, same lines and output template). Assert `toBe` equality. Also check that the result starts with `http://127.0.0.1:10000/devstoreaccount1/staging/d/f?` and contains `spr=https%2Chttp`.
   - **Azurite recipient parity:** a minted read URL with `rscd` is compared with an oracle copy of today's `browserDownloadUrl` (`RecipientStore.ts:60-87`), with origin = AZURITE_LOOPBACK_ORIGIN, using a host.docker.internal:10000 minted URL to prove the origin override. Assert `toBe` equality.
   - Verify: `npx vitest run src/core/blob`.
9. **StagingList.** MODIFY `src/wa.web/src/features/landing/StagingList.tsx`:
   - Delete `:36-37` (the constants) and `:53-86` (`reSignUploadUrl`, `toBase64`, and the body of `reSignForHttp`).
   - Keep a tiny `reSignForHttp` that maps draft.files with `await toBrowserBlobUrl(f.uploadUrl)`, or inline that map at `:166`. Either way `:166` stays one `start(...)` call.
   - Add `import { toBrowserBlobUrl } from '../../core/blob/azuriteSas'` as the last import (D-5).
   - Fix the doc comment so it says "Azurite only; production URLs pass through".
   
   MODIFY `src/wa.web/src/features/landing/StagingList.test.tsx`: add a fact in the existing send-flow describe (`:285-…`). Stub the draft response with `makeDraft(['https://acct.blob.core.windows.net/staging/x?sv=2024&sp=cwr&sig=abc'])`, capture the argument of the stubbed `start` (currently `start: async () => {…}`, `:300-304`), and assert draft.files[0].uploadUrl is exactly the minted string. The existing facts stay green: their fixture `http://127.0.0.1:10000/devstoreaccount1/...` is still treated as Azurite.
10. **RecipientStore.** MODIFY `src/wa.web/src/features/recipient/RecipientStore.ts`:
    - Delete `:43-87` (AZURITE_* constants, `toBase64`, `browserDownloadUrl`).
    - Replace the calls at `:425` and `:482` with await toBrowserBlobUrl(body.url, AZURITE_LOOPBACK_ORIGIN).
    - Update the two call-site comments: "re-sign only for local Azurite; a production SAS navigates verbatim".
    - Add the import after line 3 (D-5).
    
    MODIFY `src/wa.web/src/features/recipient/RecipientPage.test.tsx`:
    - The two re-sign assertions (`:957-961` and `:1346-1349`) rely on fixture SAS_URL = 'https://azurite.example.com/transfers/fake-sas?sig=abc' (`:901`, `:1273`). That fixture is no longer Azurite, so change those two constants to `'http://127.0.0.1:10000/transfers/fake-sas?sig=abc'`. The expected prefix `http://127.0.0.1:10000/devstoreaccount1/transfers/fake-sas?` still holds.
    - Leave `:1047` (growth loop; no href assertion) and `RecipientPage.ads.test.tsx:48` unchanged. They now navigate verbatim, which is harmless.
    - Add one fact in the T-003-06 describe: endpoint 6 returns `https://acct.blob.core.windows.net/transfers/t/f?sv=2024&sp=r&sig=abc`, and `assignedHref` must be **exactly** that string (production pass-through, per-file).
    - Verify: `npx vitest run src/features/recipient src/features/landing src/core/blob`.
11. **Full local gate** (AGENT.md §4; Docker is down, so integration is build-only here and runs in CI):
    ```
    dotnet test tests/wa.domain.unit
    dotnet test tests/wa.application.unit
    dotnet test tests/wa.workers.unit
    dotnet build tests/wa.api.integration
    dotnet format src/wa.slnx (verify-only flag, exactly as AGENT.md §4)
    cd src/wa.web; npm run lint; npm run test:run; npm run build
    node ../../scripts/check-theme-tokens.mjs; node ../../scripts/check-contrast.mjs
    node ../../scripts/check-telemetry-pii.mjs; node ../../scripts/check-telemetry-pii.mjs selftest
    ```
    Then make one commit, `fix: CHORE-staging-blockers — signup verifies password on known email; Azurite-only SAS re-sign`.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.application/UseCases/Auth/PasswordSignIn.cs` | CREATE | internal static `Accepts(AppUser, string)`: deleted / null hash / failed PBKDF2 verify → false (D-1, D-2) |
| `src/wa.application/UseCases/Auth/LoginHandler.cs` | MODIFY | `:47-56` use `PasswordSignIn.Accepts`; drop the unused Identity using; summary notes deleted account |
| `src/wa.application/UseCases/Auth/SignupHandler.cs` | MODIFY | known email → `SignInExistingAsync` (verify, `login_failed` + `LoginWrongPasswordException`, own plan via `GetByIdAsync`); `freePlan` only on create; docs |
| `src/wa.api/Endpoints/AuthEndpoints.cs` | MODIFY | signup route: catch `LoginWrongPasswordException` → `BusinessException(ProblemCodes.WrongPassword, …)`; comment |
| `tests/wa.application.unit/Auth/SignupHandlerTests.cs` | MODIFY | real hash in the known-email fact; 5 new facts (right / wrong / null-hash / deleted / own-plan) |
| `tests/wa.application.unit/Auth/LoginHandlerTests.cs` | MODIFY | `Login_SoftDeletedAccount_ThrowsWrongPassword_EmitsLoginFailed` |
| `tests/wa.api.integration/SignupEndpointTests.cs` | MODIFY | existing email + wrong password → 401 WRONG_PASSWORD, no token; soft-deleted → 401 |
| `src/wa.web/src/core/blob/azuriteSas.ts` | CREATE | AZURITE_LOOPBACK_ORIGIN, `isAzuriteBlobUrl`, `toBrowserBlobUrl` (D-4) |
| `src/wa.web/src/core/blob/azuriteSas.test.ts` | CREATE | truth table; production verbatim; upload and recipient oracle parity |
| `src/wa.web/src/features/landing/StagingList.tsx` | MODIFY | delete `:36-37` + `:53-86` re-sign code; map uploadUrls through `toBrowserBlobUrl`; import last |
| `src/wa.web/src/features/landing/StagingList.test.tsx` | MODIFY | production uploadUrl reaches `start()` verbatim |
| `src/wa.web/src/features/recipient/RecipientStore.ts` | MODIFY | delete `:43-87`; `:425`/`:482` → toBrowserBlobUrl(body.url, AZURITE_LOOPBACK_ORIGIN); import after line 3 |
| `src/wa.web/src/features/recipient/RecipientPage.test.tsx` | MODIFY | SAS_URL at `:901` and `:1273` → `http://127.0.0.1:10000/transfers/fake-sas?sig=abc`; new production-verbatim fact |

**`SignupHandler.cs` (current, `:36-83`)**, the block being replaced:
```csharp
    public async Task<SignupResult> Handle(SignupCommand request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!IsValidEmail(email))
            throw SignupValidationException.ForInvalidEmail();
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            throw SignupValidationException.ForShortPassword();

        var freePlan = await _plans.GetAsync(Plan.DefaultCode, cancellationToken);
        var existing = await _users.FindByEmailAsync(email, cancellationToken);

        AppUser user;
        if (existing is null)
        {
            var hasher = new PasswordHasher<string>();
            var userId = Guid.NewGuid();
            user = new AppUser { Id = userId, Email = email, EmailConfirmed = false, DisplayName = DeriveDisplayName(email),
                PasswordHash = hasher.HashPassword(userId.ToString("n"), request.Password), PlanId = freePlan.Id, Theme = "system" };
            await _users.CreateAsync(user, cancellationToken);
            _telemetry.LogEvent(TelemetryEvents.UserCreated);
            LogUserCreated(_logger, userId);
        }
        else
        {
            user = existing; // EC-008-4: known email (any case) → sign into that row, no duplicate   <-- THE BUG
        }

        _telemetry.LogEvent(TelemetryEvents.LoginSuccess);
        return new SignupResult(user.Id, user.Email, user.DisplayName, user.Theme, freePlan.Code, _tokens.Mint(user.Id));
    }
```
(The snippet is condensed, and its comments are trimmed. Keep the real file's comments where they still hold.) `using Microsoft.AspNetCore.Identity;` stays because the create branch hashes. `_plans` is `IPlansStore`, which has `GetByIdAsync(Guid, CancellationToken)` returning `Task<Plan?>` (used at `LoginHandler.cs:59`).

**`LoginHandler.cs` (current, `:47-56`):**
```csharp
        var hasher = new PasswordHasher<string>();
        if (user.PasswordHash is null
            || hasher.VerifyHashedPassword(user.Id.ToString("n"), user.PasswordHash, request.Password) is not
            (PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded))
        {
            _telemetry.LogEvent(TelemetryEvents.LoginFailed);
            throw LoginWrongPasswordException.ForWrongPassword();
        }
```

**`AuthEndpoints.cs` signup route (current, `:24-45`):** a try around `sender.Send(cmd, ct)` returns `Results.Json(new { userId = result.UserId.ToString("n"), email, displayName, theme, planCode, sessionToken }, statusCode: 200)`. Its only catch is `catch (SignupValidationException ex) { throw new BusinessException(ProblemCodes.Validation, ex.Message, ex); }`. Add the `LoginWrongPasswordException` catch after it, copying login's `:145-149`. The usings already cover `wa.application.UseCases.Auth` and `ProblemCodes`.

**`SignupHandlerTests.cs`:** the stubs are at `:136-165`. `StubAppUserStore.ExistingUser` is matched by exact lowercased email, and `GetByIdAsync` is already implemented. `StubPlansStore.GetByIdAsync` returns `Plan` when the id matches (`:162-163`). Add an `int GetAsyncCalls` counter to `GetAsync`. Telemetry asserts use `_telemetryLogger.Entries` with `e.RenderedText == TelemetryEvents.X`.

**`StagingList.tsx` (current, `:36-37`, `:57-86`, `:166`):** AZURITE_ACCOUNT_KEY_B64 and AZURITE_ACCOUNT_NAME constants. `reSignForHttp(draft)` imports the HMAC key and maps draft.files through `reSignUploadUrl(f.uploadUrl, key)`. `reSignUploadUrl` builds the 16 lines ('cwr', st, se, '/blob/' + AZURITE_ACCOUNT_NAME + u.pathname, '', '', 'https,http', '2024', 'b', '', '', '', '', '', '', '') and returns ${u.origin}/${AZURITE_ACCOUNT_NAME}${u.pathname}?sv=2024&spr=https%2Chttp&st=…&se=…&sr=b&sp=cwr&sig=…. Call site: `await useUploadEngine.getState().start(await reSignForHttp(draft))`. The `Draft` type comes from `core/upload/UploadEngine` (already imported at `:7`). Copy this function verbatim into the parity oracle in `azuriteSas.test.ts` **before** deleting it.

**`RecipientStore.ts` (current, `:43-87`, `:425`, `:482`):** constants AZURITE_BLOB_ORIGIN = 'http://127.0.0.1:10000', AZURITE_ACCOUNT_NAME and AZURITE_ACCOUNT_KEY_B64; `toBase64`; and `browserDownloadUrl(mintedUrl)`, which reads `sv/st/se/sp/sr/rscd` (defaults `'2024'`/`''`/`''`/`'r'`/`'b'`/`''`). It builds the lines `sp, st, se, /blob/devstoreaccount1{path}, '', '', 'https,http', sv, sr, '', '', '', rscd, '', '', ''` and returns ${AZURITE_BLOB_ORIGIN}/devstoreaccount1${path}?sv=${sv}&spr=https%2Chttp&st=…&se=…&sr=b&sp=${sp}&rscd=…&sig=…. The call sites are `const href = await browserDownloadUrl(body.url)` (download-all/zip, `:425`) and window.location.href = await browserDownloadUrl(body.url) (per-file, `:482`). Copy `browserDownloadUrl` verbatim into the second oracle before deleting it.

**`RecipientPage.test.tsx`:** the describes at `:900` and `:1272` each declare const SAS_URL. The href assertions at `:957-961` and `:1346-1349` check `startsWith('http://127.0.0.1:10000/devstoreaccount1/transfers/fake-sas?')` plus `spr=https%2Chttp`. A fake window.location captures `assignedHref` (`:935-944`); reuse that pattern for the new fact.

## 4. Open Questions / Risks

- **Q-1 (enumeration, accepted, D-3):** after this change, signup still tells a caller whether an email is registered: 401 for a known email versus 200 that creates an account. Fully closing that needs a confirm-email-first signup, which would be a product change (FR-008-3) and is not planned here. **Orchestrator follow-up:** record it in Open-Decisions Part 3 as an accepted residual.
- **Q-2 (functional, not security):** signup sets no `wa.session` cookie (`AuthEndpoints.cs:24-45`), unlike login and redeem. The SPA writes only its localStorage profile mirror (`AuthPage.tsx:102`), so right after a web signup, cookie-gated calls (`/auth/me`, My Files, attribution on `/send`) run as a guest until the next login. Adding the cookie on signup would reuse login's TA-9.1 flags verbatim, but it is outside this brief. **Recommend** a small follow-up CHORE. It is not done here, to keep the change bounded.
- **Risk (deleted-email lock-out):** a soft-deleted email cannot sign up again, because the signup returns 401 and the unique index still holds the row. This is unchanged in effect: today it would have signed into the deleted row. Re-registration depends on the erasure hard-delete (US-047) freeing the row.
- **Risk (merge with #713):** a textual conflict is expected at `StagingList.tsx:35-37`. Resolve it as described in D-5. RecipientStore should merge cleanly.
- **Risk (local Docker API):** when the API runs in docker-compose, it mints host.docker.internal:10000 URLs (`docker-compose.local.yml:49`). These are still detected as Azurite (port 10000) and behave exactly as today: StagingList keeps the minted origin and RecipientStore rewrites to `127.0.0.1:10000`.
- **Orchestrator doc follow-ups (not Coder scope):**
  - F-TRF-008 spec EC-008-4 row (`docs/features/Phase 0-MVP/F-TRF-008/F-TRF-008-accounts.md:50`) and T-008-01 task file steps (`US-008-01-signup-password/T-008-01-signup-endpoint.md:16,34`): change to "known email signs in **only with the correct password**; otherwise 401 WRONG_PASSWORD (same as login)".
  - Open-Decisions Part 3: append a row that supersedes the 2026-10-20 "unconditional" Azurite re-sign rows (re-sign only when `isAzuriteBlobUrl`) and the D-2 soft-deleted-login rule.
  - `docs/REPO-FACTS.md`: no closed-set change (ProblemCodes, TelemetryEvents, storage keys and endpoints are all unchanged).
