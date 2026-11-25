namespace wa.application.Ports;

/// <summary>US-047-01 (FR-047-2, TA-3.6): 24 h read SAS on one data-export zip (exports container, permissions r, attachment).</summary>
public interface IExportSasMinter
{
    /// <summary>Mints the export download.</summary>
    /// <param name="userId">Identifier of the user.</param>
    /// <param name="exportId">Identifier of the export.</param>
    /// <returns>The URI.</returns>
    Uri MintExportDownload(Guid userId, Guid exportId);
}
