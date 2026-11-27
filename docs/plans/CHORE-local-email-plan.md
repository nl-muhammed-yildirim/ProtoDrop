# CHORE-local-email — plan

Source: GitHub issue #724 (scope B + C, compose + docs). Decision: `docs/Open-Decisions-and-Constants.md` Part 3, row 2026-11-27 "Local email delivery (CHORE-local-email, owner decision)". It approves exactly four new local-only keys (`Wa:EmailFunction:BaseUrl`, `Wa:Email:HandOffSignKey`, `Wa:Email:Smtp:Host`, `Wa:Email:Smtp:Port`), no new package (SMTP through System.Net.Mail), and a Mailpit container. Branch `feature/CHORE-local-email` (origin/develop + aae7254). Production path (Service Bus + Communication Services) is unchanged.

## 1. Goal

Make the local Docker stack deliver real mail: the API hands every `core` event to the workers over a signed HTTP POST, and the workers send through SMTP into Mailpit. **Exit check:** issue #724 items 1–4. After docker compose -f docker-compose.local.yml up -d (plus the compose build flag), a magic link and a transfer notification appear at http://localhost:8025, and the magic link signs the user in. The unit facts in §3 are green, and the full AGENT.md §4 gate passes.

## 2. Design & Decisions

**D-1. One shared hand-off contract (TA-5.2, TA-0.2 rule 7).** New static class `src/wa.infrastructure/Events/EmailHandOff.cs`. Both the API (wa.infrastructure) and the workers (they reference wa.infrastructure, see `src/wa.workers/WorkerServices.cs:15-16`) use it, so the header name, keys, route and signature live in one place:
- `BaseUrlKey = "Wa:EmailFunction:BaseUrl"`, `SignKeyKey = "Wa:Email:HandOffSignKey"`, `HeaderName = "x-email-sign"`.
- `Route = "events/email"`: the workers HttpTrigger route.
- `RelativeUrl = "api/events/email"`: the Functions default `routePrefix` is `api`, and `src/wa.workers/host.json` does not override it.
- `string Sign(ReadOnlySpan<byte> body, string key)` returns lowercase hex of HMACSHA256.HashData(UTF-8 key, body).
- `bool Verify(byte[] body, string? provided, string? key)` returns false when key or provided is null or empty. Otherwise it compares UTF-8 bytes of `Sign(body,key)` and `provided` with `CryptographicOperations.FixedTimeEquals`.

Tradeoff: the zip trigger sends the raw shared key as the header value (`src/wa.application/UseCases/Transfers/GetDownloadAllUrlHandler.cs:121`, checked at `src/wa.workers/ZipFunction.cs:63-67`). The issue asks for an HMAC header, so this one signs the exact body bytes. The key never travels, and a tampered body fails. The timing-safe compare is the same as the zip trigger's.

**D-2. `HttpHandOffEventPublisher : IEventPublisher` (TA-5.2, TA-4.1.7).** New file `src/wa.infrastructure/Events/HttpHandOffEventPublisher.cs`. It is a `sealed partial` class with primary ctor `(HttpClient httpClient, Uri baseUrl, string signKey, ILogger<HttpHandOffEventPublisher> logger)` and keeps `_x = x` fields.
- `PublishAsync`:
  1. `json = JsonSerializer.Serialize(envelope)`, the same single source of the byte shape as `SbEventPublisher.cs:50` and `InMemoryEventPublisher.cs:29`. `body = Encoding.UTF8.GetBytes(json)`.
  2. POST `new Uri(_baseUrl, EmailHandOff.RelativeUrl)` with `ByteArrayContent(body)`, content type `application/json`, and header `x-email-sign = EmailHandOff.Sign(body, _signKey)`. Use a linked CTS with `CancelAfter(HandOffTimeout)`, where `private static readonly TimeSpan HandOffTimeout = TimeSpan.FromSeconds(10)`, mirroring `SbEventPublisher.PublishTimeout` (TA-4.1.7). This is a transport timeout, not a Limits-Registry value.
  3. A 2xx response returns `new DomainEventRecord { EventId = envelope.EventId, IsPublished = true }`.
  4. A non-2xx response logs `LogHandOffRejected(_logger, envelope.EventId, wireName, (int)response.StatusCode)` and returns `IsPublished = false`.
  5. Any exception, including timeout and cancel, logs `LogHandOffFailed(_logger, ex, envelope.EventId, wireName)` and returns `IsPublished = false`. It never throws.
- `wireName = EventTypeConverter.WireName(envelope.Type)`, the same as `SbEventPublisher.cs:56`.
- No outbox row is written, so the behaviour matches InMemory, and every call site still awaits the publish.
- The call is awaited (not Task.Run), because `SendTransferHandler.cs:185-188` commits before it publishes, and awaiting keeps cancellation and timeouts bounded. A failed hand-off never fails the request (issue: "fire-and-log").
- Every event type is forwarded. The `email` subscription has no filter, and the dispatcher ignores types it does not route.
- Log templates use ids and wire names only, never the payload (TA-9.4, PII guard R7). Shape: `[LoggerMessage(Level = LogLevel.Error, Message = "Email hand-off failed for event {EventId} type {EventType}")] private static partial void LogHandOffFailed(ILogger logger, Exception exception, Guid eventId, string eventType);`. The rejected variant is a Warning with `status={StatusCode}`.

**D-3. API selection (TA-0.2 rule 7, TA-1.4).** Extract the block at `src/wa.api/ApiServices.cs:40-48` into a new `public static IServiceCollection AddEventPublisher(this IServiceCollection services, IConfiguration configuration)` on `ApiServices`. Call it at the same position, so registration order is unchanged. Precedence:
1. `Wa:ServiceBus:ConnectionString` set → `SbEventPublisher`, exactly as today, even when the BaseUrl is also set (exit item 3).
2. Otherwise `Wa:EmailFunction:BaseUrl` blank → `InMemoryEventPublisher` (all integration tests stay on this path).
3. Otherwise the BaseUrl is set:
   - if `!EmailSenderSelector.IsLocalOrDevelopment(configuration)` → throw `InvalidOperationException`. The message names `Wa:EmailFunction:BaseUrl` and the environment, never a value. This is the fail-fast pattern from `EmailSenderSelector.cs:40-53`.
   - if the sign key is blank → throw, naming `Wa:Email:HandOffSignKey`.
   - else register `HttpHandOffEventPublisher(new HttpClient(), new Uri(baseUrl.TrimEnd('/') + "/"), key, logger)`.

In compose the API sees `ASPNETCORE_ENVIRONMENT=Development` through configuration, because WebApplication.CreateBuilder adds unprefixed env vars.

**D-4. Shared workers handling path (TA-6.7, TA-6.2).** The dispatch moves out of `EmailFunction` into a deep module that both triggers call:
- `src/wa.workers/Interfaces/IEmailEnvelopeDispatcher.cs`: `Task DispatchAsync(string envelopeJson, int deliveryCount, CancellationToken ct)`.
- `src/wa.workers/EmailEnvelopeDispatcher.cs`, a `public sealed partial` class:
  - takes the four handlers and `ILogger<EmailEnvelopeDispatcher>`;
  - its body is today's `EmailFunction.cs:51-85` verbatim: deserialize, null → return, trace scope, the type switch, try / `LogEventFailed` / rethrow. `IsForgotPasswordPayload` (`:92-97`) and `LogEventFailed` (`:99-100`) move with it;
  - `public const int MaxDeliveryCount = 5` (was private at `:35`).
- `EmailFunction` keeps `[Function("f-email")]` and its `ServiceBusTrigger` attribute unchanged. Its ctor becomes `(IEmailEnvelopeDispatcher dispatcher)` and its body becomes `await _dispatcher.DispatchAsync(message.Body.ToString(), message.DeliveryCount, context.CancellationToken);`. It is no longer `partial`.
- New `src/wa.workers/EmailHandOffFunction.cs`, `public partial class EmailHandOffFunction(IEmailEnvelopeDispatcher dispatcher, IConfiguration config, ILogger<EmailHandOffFunction> logger)`. It has `[Function("f-email-handoff")] Task<HttpResponseData> Run(FunctionContext context, [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = EmailHandOff.Route)] HttpRequestData req)`:
  1. Outside Local/Development (`EmailSenderSelector.IsLocalOrDevelopment(_config)`) → `JobAuth.CreateProblem(req, 404, ProblemCodes.NotFound, "Not found.")`. The function is invisible in prod and the key is never read.
  2. Copy `req.Body` into a MemoryStream to get `body` bytes. Read the header with `req.Headers.TryGetValues(EmailHandOff.HeaderName, out var v)`, never GetValues (it throws; see `JobAuth.cs:18`). Take `provided = v is null ? null : string.Join(",", v)`.
  3. `!EmailHandOff.Verify(body, provided, _config[EmailHandOff.SignKeyKey])` → 401 `ProblemCodes.Unauthenticated` "Invalid or missing signature." This is the same code as `ZipFunction.cs:67`, so the closed list is reused (TA-4.1.3).
  4. `await _dispatcher.DispatchAsync(Encoding.UTF8.GetString(body), EmailEnvelopeDispatcher.MaxDeliveryCount, context.CancellationToken)`. HTTP has no broker retry, so this is the final attempt and the handlers' last-attempt failure reporting fires.
  5. Success → `JobAuth.CreateSuccess(req, new { dispatched = true })`. An exception → `LogHandOffDispatchFailed(_logger, ex)` and 500 `ProblemCodes.Internal`.
- `WorkerServices` registers `IEmailEnvelopeDispatcher` → `EmailEnvelopeDispatcher` as a singleton factory. Function classes are activated from DI, so both triggers resolve it.

**D-5. SMTP sender (TA-6.7, TA-9.4).** New `src/wa.infrastructure/Email/SmtpEmailSender.cs`, `public sealed class SmtpEmailSender(string host, int port, string fromAddress) : IEmailSender`.
- `public static MailMessage CreateMailMessage(EmailMessage message, string fromAddress)` builds:
  - From = `fromAddress`, the configured address, the same as `CommunicationHubEmailSender.cs:36-39`;
  - To = `message.To`;
  - `ReplyToList.Add` only when ReplyTo is non-empty (`:41-42`);
  - Subject with UTF-8 SubjectEncoding;
  - two AlternateViews: text/plain (TextBody) first, then text/html (HtmlBody), both UTF-8. The last alternative is the preferred one.

  It is public so the test can check the shape without a server.
- `SendAsync`: `using var mail = CreateMailMessage(...)`, `using var client = new SmtpClient(_host, _port)` (no SSL, no credentials: Mailpit), then `await client.SendMailAsync(mail, ct)`. No logging, so no PII surface.

**D-6. Selector + guard.** In `EmailSenderSelector`:
- Add `SmtpSender = "smtp"`, `SmtpHostKey`, `SmtpPortKey`.
- Add `public static bool IsLocalOrDevelopment(IConfiguration)`, lifted from `:45-48`.
- `EnsureSenderAllowed` refuses `log` **or** `smtp` outside Local/Development. The message names `Wa:Email:Sender` and the chosen value.
- `Create` gets a new `smtp` arm between `log` and Communication Hub. The host must be non-blank, else throw naming `Wa:Email:Smtp:Host`. The port must parse as an int (invariant culture) in 1..65535, else throw naming `Wa:Email:Smtp:Port`. The From address uses the existing `configuration[FromAddressKey] ?? DefaultFromAddress`.
- The `log` and default arms are unchanged.
- `WorkerServices.cs:169` already calls `EnsureSenderAllowed` eagerly, so the workers host refuses to start with `smtp` in prod.

**D-7. Compose + docs.** Edit only the `wa.api` and `wa.workers` services and add a new `mailpit` service. PR #723 edits `wa.web` in the same file, so stay out of it. Details are in step 6. `docs/AGENT.md` §5.1 and §5.2 are updated in step 7.

## 3. Implementation Plan

1. **Hand-off contract + publisher (D-1, D-2).** Create `EmailHandOff.cs` and `HttpHandOffEventPublisher.cs`. Tests:
   - `tests/wa.application.unit/Events/EmailHandOffTests.cs`: Sign is deterministic 64-hex; Verify is true for the right body+key; false for a tampered body, a wrong key, and null/empty provided or key.
   - `tests/wa.application.unit/Events/HttpHandOffEventPublisherTests.cs`, with a nested capturing `HttpMessageHandler` stub and `CapturingLogger<T>` from `tests/wa.application.unit/CapturingLogger.cs:9`:
     - (a) **exit 3:** a 200 stub. The request is POST to `http://workers.test:8081/api/events/email`. Content bytes equal `Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope))`, content type is application/json, header `x-email-sign == EmailHandOff.Sign(thoseBytes, key)`, and `IsPublished` is true.
     - (b) a 500 stub → `IsPublished` false, no throw, one Warning entry carrying the event id.
     - (c) a stub that throws HttpRequestException → false, no throw, one Error entry. No entry value contains the payload email `a@b.test`.

   Verify: dotnet test tests/wa.application.unit filtered to "FullyQualifiedName~EmailHandOff|FullyQualifiedName~HttpHandOffEventPublisher".
2. **API selection (D-3).** Modify `src/wa.api/ApiServices.cs`. Create `tests/wa.api.integration/Events/ApiEventPublisherSelectionTests.cs`. It needs no containers. Use `new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(c).AddApiServices(c)` with in-memory config: `ConnectionStrings:WaDb`, `Wa:Blob:AccountUrl` = `http://127.0.0.1:10000/devstoreaccount1`, `Wa:Blob:AccountKey` = base64 of 64 zero bytes, `Wa:Jwt:Secret` = 64 hex. Add any other key that AddApiServices reads eagerly if a fact throws for a missing one. Then resolve `IEventPublisher`:
   - (a) **exit 3:** SB string + BaseUrl + key + `ASPNETCORE_ENVIRONMENT=Development` → `SbEventPublisher`. Use the fake SB string from `tests/wa.workers.unit/WorkerServicesTests.cs:21`.
   - (b) nothing set → `InMemoryEventPublisher`.
   - (c) BaseUrl + key + Development → `HttpHandOffEventPublisher`.
   - (d) a theory over Production and unset environment with the BaseUrl set → `AddApiServices` throws InvalidOperationException whose message contains `Wa:EmailFunction:BaseUrl`.
   - (e) BaseUrl + Development, no key → throws, message contains `Wa:Email:HandOffSignKey`.

   Verify: dotnet test tests/wa.api.integration filtered to "FullyQualifiedName~ApiEventPublisherSelectionTests".
3. **SMTP sender + selector (D-5, D-6).** Create `SmtpEmailSender.cs`. Modify `EmailSenderSelector.cs`. Extend `tests/wa.workers.unit/Email/EmailSenderSelectorTests.cs` (exit 3):
   - `Create_Smtp_Development_ReturnsSmtpEmailSender`: host `mailpit`, port `1025`, plus the AZURE_FUNCTIONS_ENVIRONMENT, DOTNET_ENVIRONMENT and ASPNETCORE_ENVIRONMENT variants, mirroring `:43-56`.
   - `Create_Smtp_OutsideDevelopment_Throws`: theory null/Production/Staging; the message contains `Wa:Email:Sender`.
   - `EnsureSenderAllowed_Smtp_InProduction_Throws`.
   - `Create_Smtp_MissingHost_ThrowsNamingKey`.
   - `Create_Smtp_BadPort_ThrowsNamingKey`: theory null / "abc" / "0" / "70000".
   - Existing facts stay unchanged.

   Create `tests/wa.workers.unit/Email/SmtpEmailSenderTests.cs` for `CreateMailMessage`: From = configured, To, ReplyTo present/absent, Subject, two views in the order text/plain then text/html with the right content.

   Verify: dotnet test tests/wa.workers.unit filtered to "FullyQualifiedName~EmailSenderSelector|FullyQualifiedName~SmtpEmailSender".
4. **Shared dispatcher + HTTP trigger (D-4).** Create `Interfaces/IEmailEnvelopeDispatcher.cs`, `EmailEnvelopeDispatcher.cs` and `EmailHandOffFunction.cs`. Modify `EmailFunction.cs` and `WorkerServices.cs`, adding the registration after the `SendBillingEmailHandler` block at `:203-209`.
   - Update `tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs:41,96-104`: the tripwire now inspects `typeof(EmailEnvelopeDispatcher)`; rename the fact to `EmailEnvelopeDispatcher_DispatchesExactlyTheClassifiedHandlers`; fix the comment.
   - Create `tests/wa.workers.unit/Email/EmailTriggersTests.cs`. It is one test class with nested stubs: copy `StubHttpRequest` / `StubHttpResponse` / `StubHttpCookies` / `StubFunctionContext` and its children from `tests/wa.workers.unit/ZipFunctionCorrelationTests.cs:123-209`. `StubHttpRequest` takes body bytes and optional headers. Add a hand-stub `RecordingDispatcher : IEmailEnvelopeDispatcher` (no Moq) and config built with `ConfigurationBuilder().AddInMemoryCollection`. Facts (exit 3):
     - (a) missing header → 401, body `code` = `UNAUTHENTICATED`, dispatcher not called.
     - (b) wrong signature → 401, not called.
     - (c) no key configured → 401, not called.
     - (d) valid signature but `AZURE_FUNCTIONS_ENVIRONMENT=Production` → 404, not called.
     - (e) valid signature + Development → 200, dispatcher called once with the exact body string and `deliveryCount == EmailEnvelopeDispatcher.MaxDeliveryCount`.
     - (f) dispatcher throws → 500 `INTERNAL`.
     - (g) `EmailFunction.Run` with `ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString(json), deliveryCount: 3)` → the dispatcher gets the same json and 3. This proves both triggers share one path.
     - (h) `EmailEnvelopeDispatcher` (handlers `null!`) given an email.sent envelope JSON and given `"null"` completes without throwing.
   - Add `EmailEnvelopeDispatcher_Resolves` to `tests/wa.workers.unit/WorkerServicesTests.cs`, using `Build(InMemory(BaseSettings()))` + `("Wa:Email:Sender","log")` + `("DOTNET_ENVIRONMENT","Development")`.

   Verify: dotnet test tests/wa.workers.unit.
5. **Format + PII.** Run dotnet format src/wa.slnx, then dotnet format src/wa.slnx in verify-no-changes mode (AGENT.md §4). Run `node scripts/check-telemetry-pii.mjs && node scripts/check-telemetry-pii.mjs selftest` from the repo root.
6. **Compose.** In `docker-compose.local.yml`:
   - **wa.api:** after the zip lines (`:60-63`), add a CHORE-local-email comment and `Wa__EmailFunction__BaseUrl: "http://host.docker.internal:8081"` plus `Wa__Email__HandOffSignKey: "local-email-handoff-key-dev-only"`.
   - **wa.workers:** replace the `log` comment and line (`:81-82`) with a comment ("mail goes to Mailpit, inbox http://localhost:8025; set log to print mails to the console instead") plus `"Wa:Email:Sender": "smtp"`, `"Wa:Email:Smtp:Host": "mailpit"`, `"Wa:Email:Smtp:Port": "1025"`, `"Wa:Email:HandOffSignKey": "local-email-handoff-key-dev-only"`. Change the hardening comment (`:83`) to "Sender=log/smtp and the email hand-off are refused outside Development/Local". Change `depends_on` to `[wa.api, mailpit]`.
   - **New service** after `azurite`: `mailpit: image: axllent/mailpit:latest`, `ports: ["1025:1025", "8025:8025"]`. Use `:latest` to match `azurite` at `:12`. Workers reach it by service name on the default network.
   - Do not touch `wa.web`.

   Verify: docker compose -f docker-compose.local.yml config (quiet mode).
7. **Docs.** In `docs/AGENT.md`:
   - §5.1: add the `mailpit` service to the yaml snippet.
   - §5.2 `Wa:CommunicationHub:Connection` row: "optional locally when `Wa:Email:Sender` is `log` or `smtp`".
   - New rows: `Wa:Email:Sender` (`smtp` → Mailpit via `Wa:Email:Smtp:Host`/`Wa:Email:Smtp:Port`, compose default; `log` → workers console; both Local/Development only), `Wa:EmailFunction:BaseUrl` (optional; when set and no Service Bus, the API POSTs each event to the workers' `f-email-handoff`; Local/Development only), `Wa:Email:HandOffSignKey` (same value on API and workers).
   - `Wa:AdminEmails` row: replace "(the workers log it with `Wa:Email:Sender=log`)" with "(open it in the Mailpit inbox http://localhost:8025; with `Wa:Email:Sender=log` the workers print it to their console instead)".
8. **Manual exit check (items 1–2).** Run docker compose -f docker-compose.local.yml up -d (plus the compose build flag), then:
   - (a) Request a magic link for a new address on http://localhost:5173. It appears in http://localhost:8025, and clicking it signs in.
   - (b) Send a transfer with a recipient address. The recipient notification appears in Mailpit.
   - (c) `docker compose logs wa.workers` shows no PII, and the API logs no "Email hand-off failed" line.

   Record the result in the commit body.
9. **Gate (item 4).** Run dotnet test on tests/wa.domain.unit, tests/wa.application.unit and tests/wa.workers.unit, plus the step-2 integration filter (the full integration suite runs at feature end). Then step 5's format and PII checks. There are no wa.web changes.

### FILES FOR CODER

| File | Action | What to change |
|---|---|---|
| `src/wa.infrastructure/Events/EmailHandOff.cs` | CREATE | D-1 static contract: keys, header, Route, RelativeUrl, `Sign`, `Verify` |
| `src/wa.infrastructure/Events/HttpHandOffEventPublisher.cs` | CREATE | D-2 publisher; two `[LoggerMessage]` methods |
| `src/wa.infrastructure/Email/SmtpEmailSender.cs` | CREATE | D-5 sender + `CreateMailMessage` |
| `src/wa.infrastructure/Email/EmailSenderSelector.cs` | MODIFY | D-6: smtp consts, `IsLocalOrDevelopment`, guard covers smtp, smtp arm in `Create`, docs |
| `src/wa.api/ApiServices.cs` | MODIFY | D-3: extract `:40-48` into `AddEventPublisher`, three-way selection + guards |
| `src/wa.workers/Interfaces/IEmailEnvelopeDispatcher.cs` | CREATE | D-4 port |
| `src/wa.workers/EmailEnvelopeDispatcher.cs` | CREATE | D-4: body moved from `EmailFunction.cs:51-100` |
| `src/wa.workers/EmailFunction.cs` | MODIFY | ctor → dispatcher only; Run delegates; drop moved members and `partial` |
| `src/wa.workers/EmailHandOffFunction.cs` | CREATE | D-4 HTTP trigger `f-email-handoff` |
| `src/wa.workers/WorkerServices.cs` | MODIFY | register `IEmailEnvelopeDispatcher`; comment at `:165-168` mentions smtp |
| `tests/wa.application.unit/Events/EmailHandOffTests.cs` | CREATE | step 1 |
| `tests/wa.application.unit/Events/HttpHandOffEventPublisherTests.cs` | CREATE | step 1 (a)-(c) |
| `tests/wa.api.integration/Events/ApiEventPublisherSelectionTests.cs` | CREATE | step 2 (a)-(e) |
| `tests/wa.workers.unit/Email/EmailSenderSelectorTests.cs` | MODIFY | step 3 smtp facts |
| `tests/wa.workers.unit/Email/SmtpEmailSenderTests.cs` | CREATE | step 3 message shape |
| `tests/wa.workers.unit/Email/EmailTriggersTests.cs` | CREATE | step 4 (a)-(h) |
| `tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs` | MODIFY | tripwire inspects `EmailEnvelopeDispatcher` ctor |
| `tests/wa.workers.unit/WorkerServicesTests.cs` | MODIFY | `EmailEnvelopeDispatcher_Resolves` |
| `docker-compose.local.yml` | MODIFY | step 6 (wa.api, wa.workers, new mailpit only) |
| `docs/AGENT.md` | MODIFY | step 7 (§5.1 snippet, §5.2 rows) |

**`src/wa.infrastructure/Email/EmailSenderSelector.cs:40-75`** (current). The guard and factory are extended:
```csharp
public static void EnsureSenderAllowed(IConfiguration configuration)
{
    if (!string.Equals(configuration[SenderKey], LogSender, StringComparison.Ordinal))
        return;
    var environment = EnvironmentKeys.Select(k => configuration[k]).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Local", StringComparison.OrdinalIgnoreCase))
        return;
    throw new InvalidOperationException($"{SenderKey}={LogSender} is allowed only when ... current: '{environment ?? "(unset = Production)"}'.");
}
public static IEmailSender Create(IConfiguration configuration, ILoggerFactory loggerFactory)
{
    EnsureSenderAllowed(configuration);
    if (configuration[SenderKey] == LogSender)
        return new LogEmailSender(loggerFactory.CreateLogger<LogEmailSender>());
    var connection = configuration[ConnectionKey];
    if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException(...);
    return new CommunicationHubEmailSender(connection, configuration[FromAddressKey] ?? DefaultFromAddress, loggerFactory.CreateLogger<CommunicationHubEmailSender>());
}
```
Update the class summary (`:7-12`) and the `<returns>`/`<exception>` docs so they cover smtp. Keep `EnvironmentKeys` public; `LogEmailSender.cs:10` crefs `EnsureSenderAllowed`, so keep that name.

**`src/wa.api/ApiServices.cs:40-48`** (current). This becomes `services.AddEventPublisher(configuration);` plus the new method (XML docs: summary, params, returns, `<exception>`):
```csharp
var sbConnString = configuration["Wa:ServiceBus:ConnectionString"];
if (string.IsNullOrEmpty(sbConnString))
    services.AddSingleton<wa.application.Ports.IEventPublisher, InMemoryEventPublisher>();
else
    services.AddSingleton<wa.application.Ports.IEventPublisher>(sp => new SbEventPublisher(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<ILogger<SbEventPublisher>>(),
        sp.GetRequiredService<IDbContextFactory<WaDbContext>>(), sp.GetRequiredService<TimeProvider>()));
```
Add `using wa.infrastructure.Email;` (`wa.infrastructure.Events` is already imported at `:12`). The integration test project references wa.api (`tests/wa.api.integration/wa.api.integration.csproj:38`), so `ApiServices` is reachable.

**`src/wa.workers/EmailFunction.cs`** (current `:27-41`, `:46-86`). The handler ctor params and fields at `:28-41` and the Run body at `:51-85` move to `EmailEnvelopeDispatcher`. Keep the class summary history lines (T-006-01 / T-008-04 / T-008-05 / US-019-04) on the dispatcher, and give EmailFunction a short summary that points to it. The `using` lines for Auth/Billing/Transfers/domain/Correlation/System.Text.Json move to the dispatcher. `IDE0005` is a warning, so remove the unused usings. The Run signature stays `public async Task Run(FunctionContext context, [ServiceBusTrigger("core", "email", Connection = "Wa:ServiceBus:ConnectionString")] ServiceBusReceivedMessage message)`.

**`src/wa.workers/WorkerServices.cs:165-172`**: only the comment changes (mention smtp); `EnsureSenderAllowed` at `:169` stays. Insert before `return services;` (`:211`):
```csharp
services.AddSingleton<IEmailEnvelopeDispatcher>(sp => new EmailEnvelopeDispatcher(
    sp.GetRequiredService<SendNotificationEmailsHandler>(), sp.GetRequiredService<SendMagicLinkEmailHandler>(),
    sp.GetRequiredService<SendForgotPasswordEmailHandler>(), sp.GetRequiredService<SendBillingEmailHandler>(),
    sp.GetRequiredService<ILogger<EmailEnvelopeDispatcher>>()));
```

**`tests/wa.workers.unit/Email/EmailAudienceClassificationTests.cs:96-104`** (current):
```csharp
[Fact]
public void EmailFunction_DispatchesExactlyTheClassifiedHandlers()
{
    var wired = typeof(EmailFunction).GetConstructors().Single().GetParameters()
        .Select(p => p.ParameterType)
        .Where(t => !typeof(ILogger).IsAssignableFrom(t))
        .OrderBy(t => t.FullName);
    Assert.Equal(Handlers.Keys.OrderBy(t => t.FullName), wired);
}
```
The two assembly scans (`typeof(EmailFunction).Assembly`, `:62`, `:76`) keep working because the dispatcher is in the same assembly. `EveryEmailSenderConsumer_IsClassified` is unaffected, because the dispatcher does not take IEmailSender.

**`docker-compose.local.yml`** (current `wa.workers` env lines `:81-84`):
```yaml
      # Email is log-only in dev (no real SMTP) — read links from the workers console/Serilog.
      "Wa:Email:Sender": "log"
      # CHORE-deploy-hardening: Sender=log is refused at startup outside Development/Local.
      AZURE_FUNCTIONS_ENVIRONMENT: "Development"
```

## 4. Open Questions / Risks

- **Route prefix.** The plan assumes the default Functions prefix `api` (no `routePrefix` in `src/wa.workers/host.json`). The zip caller posts to `/zip/...` without `api/` (`GetDownloadAllUrlHandler.cs:120`), so one of the two is wrong locally. If step 8 gets a 404 from the workers, change only `EmailHandOff.RelativeUrl`. The zip path is out of scope; record it as a follow-up.
- **CA2000 on `new HttpClient()`** inside the DI lambda. If the analyzer fires (TreatWarningsAsErrors), make the publisher own the client: implement IDisposable and dispose it there. Do not add an `.editorconfig` suppression.
- **Request latency.** The API awaits the workers, which send SMTP inline: up to the 10 s timeout per publish, local only. This is accepted. Prod never selects this adapter (D-3 guard).
- **Not covered locally:** transfer.created events from the workers' own `ActivateScheduledTransfersHandler` (scheduled sends) and billing emails published inside the workers go to the workers' InMemory publisher, so they send no mail. The issue does not require them; this is a follow-up if wanted.
- **Docs close-out (orchestrator, not Coder):**
  - TA-6.2 inventory row for `f-email-handoff` (HTTP POST, local only);
  - TA-13.1 rows for the four keys;
  - `docs/REPO-FACTS.md:73`, which names the "EmailFunction ctor" tripwire and should now name `EmailEnvelopeDispatcher`.
- No escalation: every key, the container and the no-package rule are within the 2026-11-27 decision.
