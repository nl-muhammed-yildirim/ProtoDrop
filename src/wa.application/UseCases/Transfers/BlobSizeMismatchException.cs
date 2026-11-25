using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.domain;

namespace wa.application.UseCases.Transfers;

/// <summary>Thrown when a blob's actual size doesn't match the declared SizeBytes.</summary>
public sealed class BlobSizeMismatchException : Exception
{
    /// <summary>Gets the file name of the blob size mismatch exception.</summary>
    public string FileName { get; }
    /// <summary>Gets the declared bytes of the blob size mismatch exception.</summary>
    public long DeclaredBytes { get; }
    /// <summary>Gets the actual bytes of the blob size mismatch exception.</summary>
    public long ActualBytes { get; }

    /// <summary>Initializes a new instance of the <see cref="BlobSizeMismatchException"/> class.</summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="declaredBytes">The declared bytes.</param>
    /// <param name="actualBytes">The actual bytes.</param>
    internal BlobSizeMismatchException(string fileName, long declaredBytes, long actualBytes)
        : base($"File '{fileName}' is {actualBytes} bytes on the server but was declared as {declaredBytes} bytes.")
    {
        FileName = fileName;
        DeclaredBytes = declaredBytes;
        ActualBytes = actualBytes;
    }
}
