namespace wa.application.Ports;

/// <summary>TA-3.6 — mints browser-side draft-upload SAS URLs (staging container, permissions cwr, 2 h window).</summary>
public interface IBlobSasMinter
{
    /// <summary>Mints the staging PUT URL for one staged file of a draft; no network at mint time (pure HMAC over the §5.2 account values).</summary>
    /// <param name="draftId">Identifier of the draft.</param>
    /// <param name="fileId">Identifier of the file.</param>
    /// <returns>The URI.</returns>
    Uri MintDraftUpload(string draftId, string fileId);

    /// <summary>
    /// T-003-06 (TA-4.2#6): mints the 30-minute read SAS for one transfer file (transfers container, permissions r) with an
    /// original-filename Content-Disposition override (RFC 5987 attachment form — EC-003-3). The override is part of the
    /// string-to-sign, so it must be provided at mint time; the browser then streams the blob directly (no server proxying).
    /// </summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <param name="fileId">Identifier of the file.</param>
    /// <param name="respContentDisposition">The resp content disposition.</param>
    /// <returns>The URI.</returns>
    Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition);

    /// <summary>T-004-01: mints a 30-minute read SAS for the whole-transfer zip (transfers container, permissions r).</summary>
    /// <param name="transferId">Identifier of the transfer.</param>
    /// <returns>The URI.</returns>
    Uri MintRecipientZip(string transferId);
}
