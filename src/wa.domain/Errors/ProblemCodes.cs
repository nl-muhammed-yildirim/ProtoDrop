namespace wa.domain.Errors;

/// <summary>
/// The closed TA-4.1.3 machine-code list — exactly these 28 members, no others in MVP (golden rule: adding one later is an architecture change).
/// Canonical status + title are pinned here so the wire shape never depends on caller text; at M0 only NOT_FOUND, INTERNAL and RATE_LIMITED are exercised.
/// </summary>
public static class ProblemCodes
{
    /// <summary>HTTP 400: the request body or parameters failed validation.</summary>
    public const string Validation = "VALIDATION";
    /// <summary>HTTP 401: no valid session or credentials were supplied.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";
    /// <summary>HTTP 403: authenticated, but not allowed to perform the action (for example not the transfer owner or not an admin).</summary>
    public const string Forbidden = "FORBIDDEN";
    /// <summary>HTTP 404: the resource or route does not exist (also used to hide resources the caller may not see).</summary>
    public const string NotFound = "NOT_FOUND";
    /// <summary>HTTP 409: the transfer is still a draft and cannot be shared or downloaded yet.</summary>
    public const string TransferNotFinalized = "TRANSFER_NOT_FINALIZED";
    /// <summary>HTTP 422: the total transfer size is above the sender's plan limit.</summary>
    public const string TransferSizeExceeded = "TRANSFER_SIZE_EXCEEDED";
    /// <summary>HTTP 507: the sender's storage quota would be exceeded by the upload.</summary>
    public const string StorageQuotaExceeded = "STORAGE_QUOTA_EXCEEDED";
    /// <summary>HTTP 409: the transfer already served its maximum number of downloads.</summary>
    public const string MaxDownloadsReached = "MAX_DOWNLOADS_REACHED";
    /// <summary>HTTP 401: the password supplied for a protected transfer is wrong.</summary>
    public const string WrongPassword = "WRONG_PASSWORD";
    /// <summary>HTTP 410: the transfer passed its expiry time and is no longer downloadable.</summary>
    public const string TransferExpired = "TRANSFER_EXPIRED";
    /// <summary>HTTP 410: the transfer was deleted by its owner or by cleanup.</summary>
    public const string TransferDeleted = "TRANSFER_DELETED";
    /// <summary>HTTP 410: the transfer row still exists but its blobs have been physically removed.</summary>
    public const string FilesGone = "FILES_GONE";
    /// <summary>HTTP 409: an <c>Idempotency-Key</c> was reused with a different request body.</summary>
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    /// <summary>HTTP 429: the caller exceeded a rate limit; a <c>Retry-After</c> header is sent.</summary>
    public const string RateLimited = "RATE_LIMITED";
    /// <summary>HTTP 400: a Stripe webhook failed signature verification or carried an unexpected payload.</summary>
    public const string StripeWebhookMismatch = "STRIPE_WEBHOOK_MISMATCH";
    /// <summary>HTTP 409: the recipient has unsubscribed, so no email can be sent to them.</summary>
    public const string EmailUnsubscribed = "EMAIL_UNSUBSCRIBED";
    /// <summary>
    /// T-008-04 (EC-008-3): magic-link token expired, redeemed, or unknown — same 410 Gone for all three (don't leak
    /// which). Deliberate extension of the closed list: single-use token expiry is semantically 410 Gone like
    /// TransferExpired.
    /// </summary>
    public const string LinkExpired = "LINK_EXPIRED";
    /// <summary>
    /// US-019-01 (AC-019-5): Stripe API unreachable / not wired at checkout creation — retryable, nothing persisted.
    /// </summary>
    public const string BillingUnavailable = "BILLING_UNAVAILABLE";
    /// <summary>
    /// US-020-02 (FR-020-2): account is at ACTIVE_TRANSFERS_MAX on finalize/send — a paywall signal, distinct from
    /// VALIDATION.
    /// </summary>
    public const string ActiveTransfersExceeded = "ACTIVE_TRANSFERS_EXCEEDED";
    /// <summary>
    /// US-044-01 (AC-044-3, FR-044-5): PUT admin flag with a value that is not valid for the key's type — old value
    /// stays live.
    /// </summary>
    public const string FlagInvalid = "FLAG_INVALID";
    /// <summary>
    /// US-044-01 (AC-044-3, FR-044-5): PUT admin flag for a key that is not seeded — no write, no audit row.
    /// </summary>
    public const string FlagUnknown = "FLAG_UNKNOWN";
    /// <summary>
    /// US-044-02 (AC-044-2, FR-044-7): a phase-gated endpoint whose feature.* flag is off (or absent) answers 404,
    /// handler never runs.
    /// </summary>
    public const string FeatureDisabled = "FEATURE_DISABLED";
    /// <summary>US-047-01 (FR-047-1, EC-047-6): a second POST /auth/me/export while one is processing.</summary>
    public const string ExportInFlight = "EXPORT_IN_FLIGHT";
    /// <summary>US-021-04 (AC-021-4): org logo exceeds MAX_LOGO_SIZE — nothing stored.</summary>
    public const string LogoTooLarge = "LOGO_TOO_LARGE";
    /// <summary>
    /// US-021-04 (FR-021-5): org logo is not a PNG/JPEG/WebP/SVG by content sniffing — nothing stored.
    /// </summary>
    public const string LogoTypeInvalid = "LOGO_TYPE_INVALID";
    /// <summary>
    /// US-022-03 (FR-022-2): scheduledSendAt outside now+5 min .. now+14 days (server time) — nothing written.
    /// </summary>
    public const string ScheduleOutOfWindow = "SCHEDULE_OUT_OF_WINDOW";
    /// <summary>
    /// US-045-01 (EC-045-3): q longer than SearchTerm.MaxLength (100 chars) on endpoints 14 and 21 — nothing queried.
    /// </summary>
    public const string QTooLong = "Q_TOO_LONG";
    /// <summary>HTTP 500: an unexpected server error; the body carries only the correlation id, never internals.</summary>
    public const string Internal = "INTERNAL";

    private static readonly Dictionary<string, (int Status, string Title)> Table = new(StringComparer.Ordinal)
    {
        [Validation] = (400, "Validation failed"),
        [Unauthenticated] = (401, "Not authenticated"),
        [Forbidden] = (403, "Forbidden"),
        [NotFound] = (404, "Not found"), // T-056-02 exit check: unknown route → 404 NOT_FOUND
        [TransferNotFinalized] = (409, "Transfer not finalized"),
        [TransferSizeExceeded] = (422, "Transfer size exceeded"), // exact TA-4.1.3 example value
        [StorageQuotaExceeded] = (507, "Storage quota exceeded"),
        [MaxDownloadsReached] = (409, "Max downloads reached"),
        [WrongPassword] = (401, "Wrong password"),
        [TransferExpired] = (410, "Transfer expired"),
        [TransferDeleted] = (410, "Transfer deleted"),
        [FilesGone] = (410, "Files gone"),
        [IdempotencyConflict] = (409, "Idempotency conflict"),
        [RateLimited] = (429, "Rate limited"), // US-056-03 Gherkin: 429 + Retry-After
        [StripeWebhookMismatch] = (400, "Stripe webhook mismatch"),
        [EmailUnsubscribed] = (409, "Email unsubscribed"),
        [LinkExpired] = (410, "This link has expired"), // T-008-04 (EC-008-3): single-use token expired/redeemed/unknown — identical state for all three
        [BillingUnavailable] = (503, "Billing unavailable"), // US-019-01 (AC-019-5): upstream Stripe dependency unavailable, retryable
        [ActiveTransfersExceeded] = (409, "Active transfers limit reached"), // US-020-02 (FR-020-2): valid request, account state conflicts
        [FlagInvalid] = (400, "Invalid flag value"), // US-044-01 (AC-044-3): typed validation failed (EC-044-1, EC-044-5)
        [FlagUnknown] = (404, "Unknown flag"), // US-044-01 (AC-044-3): key is not a seeded flag
        [FeatureDisabled] = (404, "Feature disabled"), // US-044-02 (AC-044-2): phase gate off → indistinguishable from an absent route
        [ExportInFlight] = (409, "Export already in progress"), // US-047-01 (EC-047-6): one processing export per user
        [LogoTooLarge] = (422, "Logo too large"), // US-021-04 (AC-021-4)
        [LogoTypeInvalid] = (415, "Logo type not supported"), // US-021-04 (FR-021-5)
        [ScheduleOutOfWindow] = (422, "Schedule out of window"), // US-022-03 (FR-022-2): well-formed but out of range
        [QTooLong] = (400, "Search query too long"), // US-045-01 (EC-045-3): q over SearchTerm.MaxLength — nothing queried
        [Internal] = (500, "Internal server error") // /health's component-unreachable variant writes an explicit 503 through the same writer
    };

    /// <summary>Canonical HTTP status for a closed-list code. Unknown codes throw at call site — invalid codes fail fast, never at serialize time.</summary>
    /// <param name="code">One of the <see cref="ProblemCodes"/> constants.</param>
    /// <returns>The HTTP status code pinned for <paramref name="code"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is not in the closed list.</exception>
    public static int StatusOf(string code) => Lookup(code).Status;

    /// <summary>The RFC 7804 human-readable title pinned per code (TA-4.1.3 example style).</summary>
    /// <param name="code">One of the <see cref="ProblemCodes"/> constants.</param>
    /// <returns>The short human-readable title pinned for <paramref name="code"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="code"/> is not in the closed list.</exception>
    public static string TitleOf(string code) => Lookup(code).Title;

    private static (int Status, string Title) Lookup(string code) => Table.TryGetValue(code, out var value)
        ? value
        : throw new ArgumentException($"Unknown Problem+JSON code '{code}' — the TA-4.1.3 list is closed.", nameof(code));
}
