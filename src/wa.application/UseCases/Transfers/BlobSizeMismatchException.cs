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
    public string FileName { get; }
    public long DeclaredBytes { get; }
    public long ActualBytes { get; }

    internal BlobSizeMismatchException(string fileName, long declaredBytes, long actualBytes)
        : base($"File '{fileName}' is {actualBytes} bytes on the server but was declared as {declaredBytes} bytes.")
    {
        FileName = fileName;
        DeclaredBytes = declaredBytes;
        ActualBytes = actualBytes;
    }
}
