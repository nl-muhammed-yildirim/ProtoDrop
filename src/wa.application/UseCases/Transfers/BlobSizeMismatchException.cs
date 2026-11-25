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
    /// <summary>Name of the file whose blob size differs from the declared size.</summary>
    public string FileName { get; }
    /// <summary>Size in bytes the client declared when it created the draft.</summary>
    public long DeclaredBytes { get; }
    /// <summary>Size in bytes the staged blob actually has in storage.</summary>
    public long ActualBytes { get; }

    /// <summary>Initializes a new instance of the <see cref="BlobSizeMismatchException"/> class.</summary>
    /// <param name="fileName">Name of the file with the mismatch.</param>
    /// <param name="declaredBytes">Size the client declared, in bytes.</param>
    /// <param name="actualBytes">Size found in storage, in bytes.</param>
    internal BlobSizeMismatchException(string fileName, long declaredBytes, long actualBytes)
        : base($"File '{fileName}' is {actualBytes} bytes on the server but was declared as {declaredBytes} bytes.")
    {
        FileName = fileName;
        DeclaredBytes = declaredBytes;
        ActualBytes = actualBytes;
    }
}
