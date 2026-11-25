namespace wa.application.Ports;

/// <summary>US-047-01 (FR-047-2, TA-3.6): 24 h read SAS on one data-export zip (exports container, permissions r, attachment).</summary>
public interface IExportSasMinter
{
    Uri MintExportDownload(Guid userId, Guid exportId);
}
