using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using wa.application.Blob;
using wa.application.Limits;
using wa.application.Ports;
using wa.application.Telemetry;
using wa.application.UseCases.Transfers;
using wa.domain.Errors;
using wa.domain.Telemetry;

namespace wa.workers;

/// <summary>
/// T-004-01: f-zip Azure Function — generates a ZIP of all files in a transfer, streamed to blob storage.
/// </summary>
public class ZipFunction
{
    private readonly IBlobStore _blobStore;
    private readonly IBlobSasMinter _sasMinter;
    private readonly IZipFileSource _fileSource;
    private readonly ILimitsProvider _limits;
    private readonly WaTelemetryReporter _telemetry;
    private readonly IConfiguration _config;
    private readonly ILogger<ZipFunction> _logger;

    public ZipFunction(
        IBlobStore blobStore,
        IBlobSasMinter sasMinter,
        IZipFileSource fileSource,
        ILimitsProvider limits,
        WaTelemetryReporter telemetry,
        IConfiguration config,
        ILogger<ZipFunction> logger)
    {
        _blobStore = blobStore;
        _sasMinter = sasMinter;
        _fileSource = fileSource;
        _limits = limits;
        _telemetry = telemetry;
        _config = config;
        _logger = logger;
    }

    [Function("Zip")]
    public async Task<HttpResponseData> Run(
        FunctionContext context,
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "zip/{transferId}")] HttpRequestData req, string transferIdParam)
    {
        // Auth: check x-zip-sign header against config using timing-safe comparison
        var expectedSign = _config["Wa:Zip:SignKey"];
        var signValues = req.Headers.GetValues("x-zip-sign");
        if (expectedSign is null || signValues is null || !VerifySignature(true, string.Join(",", signValues), expectedSign))
            return CreateProblemResponse(req, 401, ProblemCodes.Unauthenticated, "Invalid or missing signature.");

        // Parse transferId as Guid → 404 if malformed (bound from route template zip/{transferId})
        if (!Guid.TryParse(transferIdParam, out var id))
            return CreateProblemResponse(req, 404, ProblemCodes.NotFound, "Transfer not found.");

        // Load files by transfer Id
        var result = await _fileSource.GetFilesByTransferIdAsync(id, default);
        var found = result.Found;
        var files = result.Files;
        if (!found || files.Count == 0)
            return CreateProblemResponse(req, 404, ProblemCodes.NotFound, "Transfer not found.");

        // Resolve limits
        var limits = await _limits.Resolve(ILimitsProvider.GuestPlanCode, default);

        // Cap check: if SUM(files.SizeBytes) > MaxZipSize → 413
        var totalSize = files.Sum(f => f.SizeBytes);
        if (totalSize > limits.MaxZipSize)
            return CreateProblemResponse(req, 413, ProblemCodes.TransferSizeExceeded, "Transfer size exceeds limit.");

        // Check if zip already exists
        var zipPath = BlobPaths.TransferZip(id);
        if (await _blobStore.ExistsAsync(zipPath, default))
        {
            var url = _sasMinter.MintRecipientZip(id.ToString("n"));
            return CreateSuccessResponse(req, url);
        }

        // Generate the zip (streaming)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string? tempPath = null;
        try
        {
            tempPath = Path.GetTempFileName();
            using (var stream = new FileStream(tempPath, FileMode.Open, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var usedNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (var file in files)
                {
                    var filePath = BlobPaths.TransferFile(id, file.FileId);
                    using var fileStream = await _blobStore.DownloadAsync(filePath, default);

                    var entryName = DeduplicateName(file.Name, usedNames);
                    var entry = zip.CreateEntry(entryName);

                    using var entryStream = entry.Open();
                    await fileStream.CopyToAsync(entryStream, 4 * 1024 * 1024); // 4MB buffer
                }
            }

            // Upload the temp file to blob
            using (var uploadStream = new FileStream(tempPath, FileMode.Open))
            {
                await _blobStore.UploadAsync(zipPath, uploadStream, default);
            }

            stopwatch.Stop();
            _telemetry.ZipGenerated(totalSize, stopwatch.ElapsedMilliseconds);

            var url = _sasMinter.MintRecipientZip(id.ToString("n"));
            return CreateSuccessResponse(req, url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate zip for transfer {TransferId}", id);
            _telemetry.LogEvent(TelemetryEvents.ZipFailed);

            // Failure cleanup: delete partial blob
            try
            {
                await _blobStore.DeleteAsync(zipPath, default);
            }
            catch (Exception deleteEx)
            {
                _logger.LogWarning(deleteEx, "Failed to delete partial zip for transfer {TransferId}", id);
            }

            return CreateProblemResponse(req, 500, ProblemCodes.Internal, "Failed to generate zip.");
        }
        finally
        {
            // Delete the temp file
            if (tempPath is not null && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temp file {TempPath}", tempPath);
                }
            }
        }
    }

    /// <summary>
    /// FR-004-5: de-dup algorithm for zip entry names — deterministic, ordered by SortOrder.
    /// </summary>
    public static string DeduplicateName(string originalName, HashSet<string> usedNames)
    {
        if (usedNames.Add(originalName)) return originalName;

        int dotIdx = originalName.LastIndexOf('.');
        string baseName = dotIdx > 0 ? originalName[..dotIdx] : originalName;
        string ext = dotIdx > 0 ? originalName[dotIdx..] : "";

        for (int i = 1; ; i++)
        {
            string candidate = $"{baseName}_{i}{ext}";
            if (usedNames.Add(candidate)) return candidate;
        }
    }

    private bool VerifySignature(bool hasHeader, string? providedSign, string expectedSign)
    {
        if (!hasHeader || providedSign is null || providedSign.Length == 0)
            return false;

        var providedBytes = System.Text.Encoding.UTF8.GetBytes(providedSign);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedSign);

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static HttpResponseData CreateProblemResponse(Microsoft.Azure.Functions.Worker.Http.HttpRequestData req, int status, string code, string details)
    {
        var payload = new
        {
            type = "https://example.com/errors/" + code.ToLowerInvariant(),
            title = ProblemCodes.TitleOf(code),
            status,
            code,
            details
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var response = req.CreateResponse((HttpStatusCode)status);
        response.Body = stream;
        return response;
    }

    private static Microsoft.Azure.Functions.Worker.Http.HttpResponseData CreateSuccessResponse(Microsoft.Azure.Functions.Worker.Http.HttpRequestData req, Uri url)
    {
        var payload = new { url = url.ToString() };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Body = stream;
        return response;
    }
}
