namespace wa.application.Ports;

/// <summary>US-047-01 (FR-047-2, TA-3.6): 24 h read SAS on one data-export zip (exports container, permissions r, attachment).</summary>
public interface IExportSasMinter
{
    /// <summary>Mints the 24-hour read URL for a finished data-export zip; the URL forces an attachment download.</summary>
    /// <param name="userId">The user the export belongs to.</param>
    /// <param name="exportId">The export request id.</param>
    /// <returns>An absolute read-only SAS URL for the export blob.</returns>
    Uri MintExportDownload(Guid userId, Guid exportId);
}
