using System.Security.Cryptography;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace wa.workers;

/// <summary>T-005-04: shared auth + response helpers for job HTTP triggers (x-job-sign header, Wa:Jobs:SignKey config).</summary>
public static class JobAuth
{
    /// <summary>Checks the <c>x-job-sign</c> header against <c>Wa:Jobs:SignKey</c> with a constant-time comparison.</summary>
    /// <param name="config">Configuration source for the signing key.</param>
    /// <param name="req">The incoming HTTP request.</param>
    /// <returns><see langword="true"/> when the header is present and equals the configured key; <see langword="false"/> when it is missing, empty or different, or when no key is configured.</returns>
    public static bool Verify(IConfiguration config, HttpRequestData req)
    {
        var expected = config["Wa:Jobs:SignKey"];
        if (expected is null || expected.Length == 0) return false;
        // HttpHeadersCollection inherits BCL HttpHeaders: GetValues throws on absent headers, so use TryGetValues.
        req.Headers.TryGetValues("x-job-sign", out var values);
        if (values is null) return false;
        var provided = string.Join(",", values);
        if (provided.Length == 0) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Create a 200 JSON success response.</summary>
    /// <param name="req">The request the response belongs to.</param>
    /// <param name="payload">Object serialized to JSON as the response body.</param>
    /// <returns>A 200 response with a JSON body.</returns>
    public static HttpResponseData CreateSuccess(HttpRequestData req, object payload)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Body = stream;
        return response;
    }

    /// <summary>Create a problem JSON response with the given status.</summary>
    /// <param name="req">The request the response belongs to.</param>
    /// <param name="status">HTTP status code of the response.</param>
    /// <param name="code">Machine-readable problem code, also used to build the <c>type</c> URL.</param>
    /// <param name="details">Human-readable explanation.</param>
    /// <returns>A JSON problem response with the given status.</returns>
    public static HttpResponseData CreateProblem(HttpRequestData req, int status, string code, string details)
    {
        var payload = new { type = $"https://example.com/errors/{code.ToLowerInvariant()}", title = code, status, code, details };
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        var response = req.CreateResponse((System.Net.HttpStatusCode)status);
        response.Headers.Add("Content-Type", "application/json");
        response.Body = stream;
        return response;
    }
}
