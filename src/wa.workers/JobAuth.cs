using System.Security.Cryptography;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;

namespace wa.workers;

/// <summary>T-005-04: shared auth + response helpers for job HTTP triggers (x-job-sign header, Wa:Jobs:SignKey config).</summary>
public static class JobAuth
{
    /// <summary>Verify x-job-sign header against Wa:Jobs:SignKey using timing-safe comparison. Returns true if valid.</summary>
    public static bool Verify(IConfiguration config, HttpRequestData req)
    {
        var expected = config["Wa:Jobs:SignKey"];
        if (expected is null || expected.Length == 0) return false;
        var values = req.Headers.GetValues("x-job-sign");
        if (values is null) return false;
        var provided = string.Join(",", values);
        if (provided.Length == 0) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(expected));
    }

    /// <summary>Create a 200 JSON success response.</summary>
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
