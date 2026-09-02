namespace wa.domain;

/// <summary>
/// TA-3.5 - the blob-storage port. ONE storage account, two containers
/// (<c>staging</c> and <c>transfers</c>). Real backends only (Azure Blobs in
/// production, Azurite for local dev per AGENT.md §5.2) live in
/// <c>wa.infrastructure</c> — the domain never names a storage SDK
/// (TA-0.2 rule 7). Container-level details (lifecycle rules, versioning,
/// soft-delete) are the storage backend's job at bootstrap time, not of
/// this contract.
/// </summary>
public interface IBlobStore
{
    /// <summary>
    /// Create the container if it does not exist (idempotent). Path must be
    /// one of the TA-3.5 container names (<c>staging</c>, <c>transfers</c>).
    /// </summary>
    /// <param name="container">Container name.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task CreateContainerIfNotExistsAsync(
        string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the container exists.
    /// </summary>
    /// <param name="container">Container name.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<bool> ContainerExistsAsync(
        string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a block blob, overwriting any existing blob at
    /// <paramref name="name"/>.
    /// </summary>
    /// <param name="container">Container name.</param>
    /// <param name="name">Blob path inside the container (TA-3.5 layout).</param>
    /// <param name="content">
    /// The stream to upload. Not disposed by the implementation — the
    /// caller owns it.
    /// </param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task UploadAsync(
        string container,
        string name,
        Stream content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a blob and return its content as a stream.
    /// </summary>
    /// <param name="container">Container name.</param>
    /// <param name="name">Blob path inside the container.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    /// <returns>
    /// A <see cref="Stream"/> with the blob content. The caller must
    /// dispose the returned stream.
    /// </returns>
    Task<Stream> DownloadAsync(
        string container,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when a blob exists at <paramref name="name"/>.
    /// </summary>
    /// <param name="container">Container name.</param>
    /// <param name="name">Blob path inside the container.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task<bool> ExistsAsync(
        string container,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete the blob if it exists (no-op if absent).
    /// </summary>
    /// <param name="container">Container name.</param>
    /// <param name="name">Blob path inside the container.</param>
    /// <param name="cancellationToken">Cancellation support.</param>
    Task DeleteAsync(
        string container,
        string name,
        CancellationToken cancellationToken = default);
}
