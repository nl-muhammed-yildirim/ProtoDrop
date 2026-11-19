namespace wa.application.Ports;

/// <summary>TA-3.6 — mints browser-side draft-upload SAS URLs (staging container, permissions cwr, 2 h window).</summary>
public interface IBlobSasMinter
{
    /// <summary>Mints the staging PUT URL for one staged file of a draft; no network at mint time (pure HMAC over the §5.2 account values).</summary>
    Uri MintDraftUpload(string draftId, string fileId);

    /// <summary>
    /// T-003-06 (TA-4.2#6): mints the 30-minute read SAS for one transfer file (transfers container, permissions r) with an
    /// original-filename Content-Disposition override (RFC 5987 attachment form — EC-003-3). The override is part of the
    /// string-to-sign, so it must be provided at mint time; the browser then streams the blob directly (no server proxying).
    /// </summary>
    Uri MintRecipientFile(string transferId, string fileId, string respContentDisposition);

    /// <summary>T-004-01: mints a 30-minute read SAS for the whole-transfer zip (transfers container, permissions r).</summary>
    Uri MintRecipientZip(string transferId);
}

/// <summary>US-047-01 (FR-047-2, TA-3.6): 24 h read SAS on one data-export zip (exports container, permissions r, attachment).</summary>
public interface IExportSasMinter
{
    Uri MintExportDownload(Guid userId, Guid exportId);
}

/// <summary>US-021-04 (TA-3.6): 30-minute inline read SAS on one org logo (avatars container, permissions r).</summary>
public interface IOrgLogoSasMinter
{
    Uri MintOrgLogo(Guid userId);
}
