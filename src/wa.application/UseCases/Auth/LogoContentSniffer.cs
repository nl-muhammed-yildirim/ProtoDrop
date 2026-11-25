using System.Text;

namespace wa.application.UseCases.Auth;

/// <summary>
/// US-021-04 (FR-021-5, type half): pure magic-byte sniffer for org logos. The client Content-Type is never trusted.
/// Returns null for anything that is not PNG / JPEG / WebP / SVG.
/// </summary>
public static class LogoContentSniffer
{
    private const int SvgScanBytes = 1024;

    private static ReadOnlySpan<byte> PngMagic => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegMagic => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Riff => "RIFF"u8;
    private static ReadOnlySpan<byte> WebPTag => "WEBP"u8;

    /// <summary>The Content-Type stored with a sniffed logo (never the client's header).</summary>
    /// <param name="type">Image type detected by <see cref="Sniff"/>.</param>
    /// <returns>The MIME type to store with the logo blob, for example <c>image/png</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="type"/> is not a defined value.</exception>
    public static string ContentTypeOf(LogoImageType type) => type switch
    {
        LogoImageType.Png => "image/png",
        LogoImageType.Jpeg => "image/jpeg",
        LogoImageType.WebP => "image/webp",
        LogoImageType.Svg => "image/svg+xml",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <summary>Detects the image type of an uploaded logo from its first bytes (PNG, JPEG and WebP signatures, or an SVG root element after an optional XML prolog and comments).</summary>
    /// <param name="head">The first bytes of the upload; at least 12 are needed for WebP detection.</param>
    /// <returns>The detected type, or <see langword="null"/> when the content is none of the supported formats.</returns>
    public static LogoImageType? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(PngMagic))
            return LogoImageType.Png;
        if (head.StartsWith(JpegMagic))
            return LogoImageType.Jpeg;
        if (head.Length >= 12 && head[..4].SequenceEqual(Riff) && head.Slice(8, 4).SequenceEqual(WebPTag))
            return LogoImageType.WebP;
        if (IsSvg(head))
            return LogoImageType.Svg;
        return null;
    }

    private static bool IsSvg(ReadOnlySpan<byte> head)
    {
        if (head.IsEmpty)
            return false;

        var text = Encoding.UTF8.GetString(head[..Math.Min(head.Length, SvgScanBytes)]);
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿')
            i++;

        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;

            if (string.CompareOrdinal(text, i, "<?", 0, 2) == 0)
            {
                var end = text.IndexOf("?>", i + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 2;
            }
            else if (string.CompareOrdinal(text, i, "<!--", 0, 4) == 0)
            {
                var end = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 3;
            }
            else
            {
                break;
            }
        }

        const string tag = "<svg";
        if (string.Compare(text, i, tag, 0, tag.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var next = i + tag.Length;
        return next < text.Length && (char.IsWhiteSpace(text[next]) || text[next] is '>' or '/');
    }
}
