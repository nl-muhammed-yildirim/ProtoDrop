using System.Text;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using wa.application.Blob;

namespace wa.infrastructure.Blob;

/// <summary>TA-9.6 — mask the sig= value in any log output (SAS never appears unmasked).</summary>
internal static class SasLogMask
{
    /// <summary>Masks the sig.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>The resulting text.</returns>
    public static string MaskSig(this Uri url) => Regex.Replace(
        url.ToString(), "sig=[^&]*", "sig=***");
}
