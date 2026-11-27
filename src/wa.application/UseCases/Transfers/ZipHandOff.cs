namespace wa.application.UseCases.Transfers;

/// <summary>
/// Shared contract between the API caller (<see cref="GetDownloadAllUrlHandler"/>) and the f-zip HTTP trigger
/// (TA-6.6 step 1): one definition of the route so the two sides cannot drift.
/// </summary>
public static class ZipHandOff
{
    /// <summary>
    /// The Functions host route prefix. The workers' host.json sets no <c>extensions.http.routePrefix</c>, so the host
    /// default <c>api</c> applies; if that setting is ever added, this value must change with it.
    /// </summary>
    public const string RoutePrefix = "api";

    /// <summary>Route template of the f-zip HTTP trigger, relative to the host prefix.</summary>
    public const string Route = "zip/{transferId}";

    /// <summary>Builds the absolute URL the API posts to in order to start zip generation.</summary>
    /// <param name="baseUrl">Configured <c>Wa:ZipFunction:BaseUrl</c> (host root, no <c>/api</c>).</param>
    /// <param name="transferId">Id of the transfer to zip.</param>
    /// <returns><c>{baseUrl}/api/zip/{id:n}</c> with exactly one slash between segments.</returns>
    public static string BuildTriggerUrl(string baseUrl, Guid transferId) =>
        $"{baseUrl.TrimEnd('/')}/{RoutePrefix}/{Route.Replace("{transferId}", transferId.ToString("n"), StringComparison.Ordinal)}";
}
