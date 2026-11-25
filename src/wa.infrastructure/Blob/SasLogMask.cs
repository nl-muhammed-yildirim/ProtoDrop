using System.Text.RegularExpressions;

namespace wa.infrastructure.Blob;

/// <summary>TA-9.6 — mask the sig= value in any log output (SAS never appears unmasked).</summary>
internal static class SasLogMask
{
    /// <summary>Replaces the value of the <c>sig</c> query parameter with asterisks so a SAS URL can be logged safely.</summary>
    /// <param name="url">URL that may carry a SAS signature.</param>
    /// <returns>The URL text with the signature value masked.</returns>
    public static string MaskSig(this Uri url) => Regex.Replace(
        url.ToString(), "sig=[^&]*", "sig=***");
}
