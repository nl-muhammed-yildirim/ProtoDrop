namespace wa.application.Ports;

/// <summary>TA-3.6 — mints browser-side draft-upload SAS URLs (staging container, permissions cwr, 2 h window).</summary>
public interface IBlobSasMinter
{
    /// <summary>Mints the staging PUT URL for one staged file of a draft; no network at mint time (pure HMAC over the §5.2 account values).</summary>
    Uri MintDraftUpload(string draftId, string fileId);
}
