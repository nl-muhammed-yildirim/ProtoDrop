namespace wa.application.UseCases.Transfers;

/// <summary>
/// Shared home for the use-case-level exceptions thrown across multiple Transfer endpoints.
/// T-007-04: moved out of CreateDraft.cs so SendTransferHandler can reuse the exact same type + wording.
/// </summary>
public sealed class TransferSizeExceededException : Exception
{
    /// <summary>Gets the total bytes of the transfer size exceeded exception.</summary>
    public long TotalBytes { get; }
    /// <summary>Gets the max transfer size of the transfer size exceeded exception.</summary>
    public long MaxTransferSize { get; }
    /// <summary>0 when the total (not a single file) is the violation.</summary>
    public long SingleFileBytes { get; }
    /// <summary>Gets the max single file of the transfer size exceeded exception.</summary>
    public long MaxSingleFile { get; }

    /// <summary>Initializes a new instance of the <see cref="TransferSizeExceededException"/> class.</summary>
    /// <param name="totalBytes">The total bytes.</param>
    /// <param name="maxTransferSize">The max transfer size.</param>
    /// <param name="singleFileBytes">The single file bytes.</param>
    /// <param name="maxSingleFile">The max single file.</param>
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

    /// <summary>Formats the gib.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The resulting text.</returns>
    internal static string FormatGib(long bytes) => (bytes / 1073741824.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " GB";
}
