using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.EmailSettings;
using wa.domain;
using wa.domain.Correlation;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.application.UseCases.Auth;

/// <summary>T-008-06 (TA-4.2#13): GET /api/v1/auth/me — the session user's profile DTO.</summary>
public sealed record GetProfileCommand(Guid UserId) : IRequest<GetProfileResult>;

/// <summary>T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial update (null = leave the field alone).</summary>
public sealed record UpdateProfileCommand(Guid UserId, string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null) : IRequest<GetProfileResult>;

/// <summary>
/// F-PRF-001 (FR-021-1 / TA-4.2#13): tri-state PATCH field. Absent from the body = <c>default</c> (<see cref="IsSet"/> false, unchanged);
/// explicit null or a blank string = clear; a string = the (trimmed) value. Needed because a plain string? cannot tell absent from null.
/// </summary>
[JsonConverter(typeof(OrgNamePatchJsonConverter))]
public readonly record struct OrgNamePatch(bool IsSet, string? Value)
{
    public static OrgNamePatch Of(string? value) => new(true, value);
}

/// <summary>Reads the org name PATCH field; HandleNull makes an explicit JSON null reach Read so it can be told apart from absence.</summary>
public sealed class OrgNamePatchJsonConverter : JsonConverter<OrgNamePatch>
{
    public override bool HandleNull => true;

    public override OrgNamePatch Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => OrgNamePatch.Of(null),
            JsonTokenType.String => OrgNamePatch.Of(reader.GetString()),
            _ => throw new JsonException("orgName must be a string or null.")
        };

    public override void Write(Utf8JsonWriter writer, OrgNamePatch value, JsonSerializerOptions options)
    {
        if (value.IsSet && value.Value is not null)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

/// <summary>T-008-06 (FR-008-8 / TA-4.2#13): DELETE /api/v1/auth/me — soft-delete + re-home transfers + account_deleted.</summary>
public sealed record DeleteAccountCommand(Guid UserId) : IRequest<DeleteAccountResult>;

/// <summary>T-008-06: wire record bound from the PATCH JSON body by Minimal API (TA-4.2a). The session supplies UserId — NOT in the body;
/// constructed at Program.cs's single construction site as new UpdateProfileCommand(userId.Value, req.DisplayName, req.Theme, req.OrgName, req.EmailNotifications).</summary>
public sealed record UpdateProfileRequest(string? DisplayName, string? Theme, OrgNamePatch OrgName = default, bool? EmailNotifications = null);

// Wire shape pinned at the endpoint's single construction site (TA-4.2a): identical to LoginResult minus SessionToken —
// the cookie already carries the session, /auth/me does not re-mint one.
public sealed record GetProfileResult(
    Guid UserId,
    string Email,
    string DisplayName,
    string Theme,
    string PlanCode,
    string? OrgName = null,
    bool EmailNotifications = true); // F-XCT-003 (FR-046-2): effective value — the column's NULL→true rule at both construction sites

public sealed record DeleteAccountResult();

/// <summary>
/// T-008-06 (D-2): the single use-case exception for the /auth/me endpoints — house style: private ctor + static factories.
/// NOT_FOUND maps to ProblemCodes.NotFound (404); VALIDATION-family maps to ProblemCodes.Validation (400) via BusinessException
/// at the endpoint. No new closed-list member is added anywhere (TA-4.1.3 stays at 18).
/// </summary>
public sealed class MeProfileException : Exception
{
    private MeProfileException(string message) : base(message) { }

    /// <summary>The request field a validation failure belongs to (TA-4.1.3 errors[].field); null when not field-specific.</summary>
    public string? Field { get; private init; }

    /// <summary>No row for the session user id (unknown OR soft-deleted) — endpoint 13 → 404 NOT_FOUND (spec's literal pin).</summary>
    public static MeProfileException ForNotFound() =>
        new(ProblemCodes.TitleOf(ProblemCodes.NotFound)); // title from the closed table — never "user not found"

    /// <summary>PATCH theme is not one of system/light/dark → 400 VALIDATION.</summary>
    public static MeProfileException ForInvalidTheme() =>
        new("theme must be one of: system, light, dark.");

    /// <summary>PATCH displayName exceeds the NVARCHAR(100) column after trimming → 400 VALIDATION.</summary>
    public static MeProfileException ForDisplayNameTooLong(string maxLength) =>
        new($"displayName must be at most {maxLength} characters after trimming.");

    /// <summary>PATCH with an all-null body → 400 VALIDATION (an empty PATCH is a silent no-op; reject it).</summary>
    public static MeProfileException ForEmptyPatch() =>
        new("Provide at least one of displayName, theme, orgName or emailNotifications.");

    /// <summary>PATCH orgName longer than ORG_NAME_MAX_LENGTH (after trimming) or containing a line break → 400 VALIDATION (field orgName).</summary>
    public static MeProfileException ForInvalidOrgName() =>
        new($"orgName must be at most {AppUser.OrgNameMaxLength} characters and contain no line breaks.") { Field = "orgName" };
}

/// <summary>T-008-06 (TA-4.2#13): GET /api/v1/auth/me — returns the profile DTO verbatim from the session user's row.</summary>
public sealed class GetProfileHandler : IRequestHandler<GetProfileCommand, GetProfileResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;

    public GetProfileHandler(IAppUserStore users, IPlansStore plans)
    {
        _users = users;
        _plans = plans;
    }

    public async Task<GetProfileResult> Handle(GetProfileCommand request, CancellationToken cancellationToken)
    {
        // FR-008-4: no row (unknown OR soft-deleted) → the endpoint maps this to 404 NOT_FOUND (spec's literal pin).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        // FR-008-4: plan code resolved via IPlansStore — mirrors LoginHandler exactly (null plan row → same not-found shape, no new code).
        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken);
        if (plan is null)
            throw MeProfileException.ForNotFound();

        return new GetProfileResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, user.OrgName, AppUser.EmailNotificationsOn(user.EmailNotificationsEnabled));
    }
}

/// <summary>
/// T-008-06 (FR-008-4 / TA-4.2#13): PATCH /api/v1/auth/me — partial profile update. Only provided fields change;
/// null argument = leave the column alone (D-1 UpdateProfileAsync contract).
/// </summary>
public sealed class UpdateProfileHandler : IRequestHandler<UpdateProfileCommand, GetProfileResult>
{
    private readonly IAppUserStore _users;
    private readonly IPlansStore _plans;
    private readonly IOrgNameStore _orgNames;
    private readonly IPlanContextAccessor _plan;
    private readonly IEmailNotificationsStore _emailNotifications;

    /// <summary>NVARCHAR(100) column width — TA-3.2 (verified in WaDbContextModelSnapshot). Single re-check site.</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>The three closed theme values (spec + entity only — NOT in the frozen Open-Decisions-and-Constants registry).</summary>
    private static readonly string[] AllowedThemes = ["system", "light", "dark"];

    public UpdateProfileHandler(IAppUserStore users, IPlansStore plans, IOrgNameStore orgNames, IPlanContextAccessor plan, IEmailNotificationsStore emailNotifications)
    {
        _users = users;
        _plans = plans;
        _orgNames = orgNames;
        _plan = plan;
        _emailNotifications = emailNotifications;
    }

    public async Task<GetProfileResult> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        // 1. Existence gate FIRST: PATCH against a dead cookie is NOT "invalid theme" — it's not-found (404 at the endpoint).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        // 2. Empty PATCH → VALIDATION (an all-null body changes nothing; reject it rather than waste a round-trip).
        if (request.DisplayName is null && request.Theme is null && !request.OrgName.IsSet && request.EmailNotifications is null)
            throw MeProfileException.ForEmptyPatch();

        // 3. theme validation (only when non-null): exactly system / light / dark → else VALIDATION.
        if (request.Theme is not null && !AllowedThemes.Contains(request.Theme))
            throw MeProfileException.ForInvalidTheme();

        // 4. displayName validation (only when non-null): after trim, length <= 100 → else VALIDATION.
        if (request.DisplayName is not null)
        {
            var trimmed = request.DisplayName.Trim();
            if (trimmed.Length > DisplayNameMaxLength)
                throw MeProfileException.ForDisplayNameTooLong(DisplayNameMaxLength.ToString());
        }

        // 4b. orgName validation (only when present in the body): no CR/LF, <= ORG_NAME_MAX_LENGTH after trim; blank = clear.
        string? orgName = null;
        if (request.OrgName.IsSet && request.OrgName.Value is { } rawOrg)
        {
            if (rawOrg.AsSpan().IndexOfAny('\r', '\n') >= 0)
                throw MeProfileException.ForInvalidOrgName();
            var trimmedOrg = rawOrg.Trim();
            if (trimmedOrg.Length > AppUser.OrgNameMaxLength)
                throw MeProfileException.ForInvalidOrgName();
            orgName = trimmedOrg.Length == 0 ? null : trimmedOrg;
        }

        // 4c. Setting a non-empty org name is a branding feature (same 403 as the logo upload); clearing is allowed on every plan.
        if (orgName is not null && !(await _plan.GetAsync(cancellationToken)).Features.Branding)
            throw OrgLogoRejectedException.ForPlan();

        // 5. Persist the partial update (D-1: null args are left alone; empty displayName falls back to the email local part).
        if (request.DisplayName is not null || request.Theme is not null)
            await _users.UpdateProfileAsync(request.UserId, request.DisplayName, request.Theme, cancellationToken);
        if (request.OrgName.IsSet)
            await _orgNames.SetOrgNameAsync(request.UserId, orgName, cancellationToken);

        // F-XCT-003 (FR-046-2): additive — absent leaves the column alone; the flag gate lives at the endpoint (FR-046-6).
        if (request.EmailNotifications is { } emailNotifications)
            await _emailNotifications.SetAsync(request.UserId, emailNotifications, cancellationToken);

        // PATCH response is byte-shaped like GET's — client can re-render without a follow-up request.
        return await ResolveResult(request.UserId, cancellationToken);
    }

    private async Task<GetProfileResult> ResolveResult(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        var plan = await _plans.GetByIdAsync(user.PlanId, cancellationToken);
        if (plan is null)
            throw MeProfileException.ForNotFound();

        return new GetProfileResult(user.Id, user.Email, user.DisplayName, user.Theme, plan.Code, user.OrgName, AppUser.EmailNotificationsOn(user.EmailNotificationsEnabled));
    }
}

/// <summary>
/// T-008-06 (FR-008-8 / TA-4.2#13): DELETE /api/v1/auth/me — soft-delete + re-home transfers in one transaction (D-1),
/// emit account_deleted via the outbox, then telemetry. Mirror RequestMagicLink's two-steps-in-one-handler shape (insert → publish).
/// </summary>
public sealed class DeleteAccountHandler : IRequestHandler<DeleteAccountCommand, DeleteAccountResult>
{
    private readonly IAppUserStore _users;
    private readonly IEventPublisher _eventPublisher;
    private readonly WaTelemetryReporter _telemetry;
    private readonly IBlobStore _blobs;
    private readonly ILogger<DeleteAccountHandler>? _logger;

    public DeleteAccountHandler(IAppUserStore users, IEventPublisher eventPublisher, WaTelemetryReporter telemetry, IBlobStore blobs,
        ILogger<DeleteAccountHandler>? logger = null)
    {
        _logger = logger;
        _users = users;
        _eventPublisher = eventPublisher;
        _telemetry = telemetry;
        _blobs = blobs;
    }

    public async Task<DeleteAccountResult> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        // 1. Existence gate: no row (unknown OR soft-deleted) → NOT_FOUND (404 at the endpoint).
        var user = await _users.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
            throw MeProfileException.ForNotFound();

        // 2. Soft-delete + transfer re-home in ONE transaction (D-1). The event publish happens AFTER this commits —
        //    the row is already soft-deleted at that point; EC-054-1's outbox re-publish covers broker failure.
        var deletedAtUtc = DateTime.UtcNow;
        var ok = await _users.SoftDeleteWithReHomeAsync(user.Id, deletedAtUtc, cancellationToken);
        if (!ok)
            throw MeProfileException.ForNotFound(); // race where a concurrent DELETE already committed — same not-found shape

        // 3. Build the TA-5.2/TA-5.3 account.deleted envelope EXACTLY as RequestMagicLink.Handle does (verbatim pattern).
        var payload = new { userId = user.Id };   // PII sweep reference (US-008-06 edge case / TA-5.3) — spec pins "carrying the userId"
        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            Type = EventType.AccountDeleted,      // 9th of 12 members — already in the frozen list, untouched file
            Version = 1,
            OccurredAtUtc = deletedAtUtc,         // the same instant passed into SoftDeleteWithReHomeAsync (the fact happened then)
            CorrelationId = GetCorrelationGuid(), // copy RequestMagicLink's private static verbatim (same convention as SendTransferHandler)
            PartitionKey = user.Email,            // email — same partitioning as the other auth.* events (RequestMagicLink uses email too)
            Payload = payload
        };

        // 4. Emit via outbox (EC-054-1: IsPublished false → re-publish picks it up on broker failure).
        await _eventPublisher.PublishAsync(envelope, cancellationToken);

        // 5. Telemetry: exactly ONE account_deleted after publish — already a member of the TA-10.2 closed set.
        _telemetry.LogEvent(TelemetryEvents.AccountDeleted);

        // 6. US-021-04 (decision 4): the org logo lives in the avatars container with no lifecycle rule — deleted with the account.
        //    The account is already deleted: a storage failure here must not turn it into a 500 (the blob is an orphan at worst).
        if (!string.IsNullOrEmpty(user.OrgLogoBlobPath))
        {
            try
            {
                await _blobs.DeleteAsync(user.OrgLogoBlobPath, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning("Org logo blob delete failed after account deletion for user {UserId}.", user.Id);
            }
        }

        return new DeleteAccountResult();
    }

    /// <summary>Derives the correlation Guid from the W3C trace-id (same convention as SendTransferHandler — copied verbatim from RequestMagicLink).</summary>
    private static Guid GetCorrelationGuid()
    {
        var traceId = CorrelationContext.CurrentRequest?.TraceId;
        if (traceId is not null && traceId.Length == 32)
        {
            var hex = traceId.Replace("-", "");
            if (hex.Length == 32 && System.Linq.Enumerable.All(hex, c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return new Guid(hex);
        }
        return Guid.NewGuid();
    }
}
