namespace wa.domain.Privacy;

/// <summary>What the erasure cascade really does today with a <see cref="PiiAttribute"/> column (T-069 encodes behavior, it does not change it).</summary>
public enum ErasureDisposition
{
    /// <summary>The column is on <c>AppUser</c> and DELETE /auth/me soft-deletes the row; the value stays until a hard delete exists.</summary>
    AccountSoftDelete,

    /// <summary>The column is on a row owned by a transfer, and the expiry job (f-delete-transfers) hard-deletes that row.</summary>
    TransferHardDelete,

    /// <summary>The value outlives erasure, either on purpose or pending a scrub follow-up; the rationale says why.</summary>
    Retained,
}
