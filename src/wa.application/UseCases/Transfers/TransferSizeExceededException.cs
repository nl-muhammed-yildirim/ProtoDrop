namespace wa.application.UseCases.Transfers;

/// <summary>Thrown when a transfer is larger than the plan allows, either in total or through one file. Shared by the create-draft and send use cases so both report the same wording.</summary>
public sealed class TransferSizeExceededException : Exception
{
    /// <summary>Total size in bytes of the selection that was refused.</summary>
    public long TotalBytes { get; }
    /// <summary>The plan's MAX_TRANSFER_SIZE in bytes.</summary>
    public long MaxTransferSize { get; }
    /// <summary>Size in bytes of the offending file; 0 when the total, not a single file, is the violation.</summary>
    public long SingleFileBytes { get; }
    /// <summary>The plan's MAX_SINGLE_FILE in bytes.</summary>
    public long MaxSingleFile { get; }

    /// <summary>Initializes a new instance of the <see cref="TransferSizeExceededException"/> class.</summary>
    /// <param name="totalBytes">Total size in bytes of the selection that was refused.</param>
    /// <param name="maxTransferSize">The plan's MAX_TRANSFER_SIZE in bytes.</param>
    /// <param name="singleFileBytes">Size in bytes of the offending file; 0 when the total is the violation.</param>
    /// <param name="maxSingleFile">The plan's MAX_SINGLE_FILE in bytes.</param>
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

    /// <summary>Formats a byte count as gigabytes with one decimal, for example <c>2.5 GB</c> (invariant culture).</summary>
    /// <param name="bytes">Size in bytes.</param>
    /// <returns>The size in gigabytes (1024-based) with a <c>GB</c> suffix.</returns>
    internal static string FormatGib(long bytes) => (bytes / 1073741824.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " GB";
}
