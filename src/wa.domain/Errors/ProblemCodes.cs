namespace wa.domain.Errors;

/// <summary>
/// The closed TA-4.1.3 machine-code list — exactly these 18 members, no others in MVP (golden rule: adding one later is an architecture change).
/// Canonical status + title are pinned here so the wire shape never depends on caller text; at M0 only NOT_FOUND, INTERNAL and RATE_LIMITED are exercised.
/// </summary>
public static class ProblemCodes
{
    public const string Validation = "VALIDATION";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string TransferNotFinalized = "TRANSFER_NOT_FINALIZED";
    public const string TransferSizeExceeded = "TRANSFER_SIZE_EXCEEDED";
    public const string StorageQuotaExceeded = "STORAGE_QUOTA_EXCEEDED";
    public const string MaxDownloadsReached = "MAX_DOWNLOADS_REACHED";
    public const string WrongPassword = "WRONG_PASSWORD";
    public const string TransferExpired = "TRANSFER_EXPIRED";
    public const string TransferDeleted = "TRANSFER_DELETED";
    public const string FilesGone = "FILES_GONE";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string StripeWebhookMismatch = "STRIPE_WEBHOOK_MISMATCH";
    public const string EmailUnsubscribed = "EMAIL_UNSUBSCRIBED";
    // T-008-04 (EC-008-3): magic-link token expired, redeemed, or unknown — same 410 Gone for all three (don't leak which).
    // Deliberate extension of the closed list: single-use token expiry is semantically 410 Gone like TransferExpired.
    public const string LinkExpired = "LINK_EXPIRED";
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
        [Internal] = (500, "Internal server error") // /health's component-unreachable variant writes an explicit 503 through the same writer
    };

    /// <summary>Canonical HTTP status for a closed-list code. Unknown codes throw at call site — invalid codes fail fast, never at serialize time.</summary>
    public static int StatusOf(string code) => Lookup(code).Status;

    /// <summary>The RFC 7804 human-readable title pinned per code (TA-4.1.3 example style).</summary>
    public static string TitleOf(string code) => Lookup(code).Title;

    private static (int Status, string Title) Lookup(string code) => Table.TryGetValue(code, out var value)
        ? value
        : throw new ArgumentException($"Unknown Problem+JSON code '{code}' — the TA-4.1.3 list is closed.", nameof(code));
}
