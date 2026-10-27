namespace wa.application.UseCases.Transfers;

/// <summary>
/// Shared home for the use-case-level exceptions thrown across multiple Transfer endpoints.
/// T-007-04: moved out of CreateDraft.cs so SendTransferHandler can reuse the exact same type + wording.
/// </summary>
public sealed class TransferSizeExceededException : Exception
{
    public long TotalBytes { get; }
    public long MaxTransferSize { get; }
    public long SingleFileBytes { get; }   // 0 when the total (not a single file) is the violation.
    public long MaxSingleFile { get; }

    internal TransferSizeExceededException(long totalBytes, long maxTransferSize, long singleFileBytes, long maxSingleFile)
        : base(BuildMessage(totalBytes, maxTransferSize, singleFileBytes, maxSingleFile))
    {
        TotalBytes = totalBytes;
        MaxTransferSize = maxTransferSize;
        SingleFileBytes = singleFileBytes;
        MaxSingleFile = maxSingleFile;
    }

    private static string BuildMessage(long totalBytes, long maxTransferSize, long singleFileBytes, long maxSingleFile) =>
        singleFileBytes > 0
            ? $"File size {FormatGib(singleFileBytes)} exceeds the {FormatGib(maxSingleFile)} limit."
            : $"Transfer size {FormatGib(totalBytes)} exceeds the {FormatGib(maxTransferSize)} limit.";

    internal static string FormatGib(long bytes) => (bytes / 1073741824.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " GB";
}
