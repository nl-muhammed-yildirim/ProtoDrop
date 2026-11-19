using System.Text;
using wa.application.UseCases.Auth;

namespace wa.application.unit.Auth;

// US-021-04 (FR-021-5): magic-byte sniffing for org logos.
public class LogoContentSnifferTests
{
    private static byte[] Bytes(params byte[] b) => b;
    private static byte[] Ascii(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] WebP(string tag) =>
        [.. Ascii("RIFF"), 0x24, 0x00, 0x00, 0x00, .. Ascii(tag), 0x56, 0x50, 0x38, 0x20];

    public static TheoryData<byte[], LogoImageType> Accepted => new()
    {
        { Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00), LogoImageType.Png },
        { Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10), LogoImageType.Jpeg },
        { WebP("WEBP"), LogoImageType.WebP },
        { Ascii("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"), LogoImageType.Svg },
        { Ascii("  \n<SVG viewBox=\"0 0 1 1\"/>"), LogoImageType.Svg },
        { [.. Bytes(0xEF, 0xBB, 0xBF), .. Ascii("<?xml version=\"1.0\"?>\n<!-- logo -->\n<svg>")], LogoImageType.Svg },
    };

    public static TheoryData<byte[]> Rejected => new()
    {
        { Ascii("GIF89a\x01\x00") },
        { Ascii("%PDF-1.7") },
        { Ascii("MZ\x90\x00") },
        { Ascii("<html><body><svg></svg></body></html>") },
        { Array.Empty<byte>() },
        { Bytes(0x89, 0x50, 0x4E) },
        { Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0B) },
        { WebP("WAVE") },
        { Ascii("<svgfoo>") },
        { Ascii("<?xml version=\"1.0\"?><!-- unterminated") },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void Sniff_KnownImage_ReturnsKind(byte[] head, LogoImageType expected) =>
        Assert.Equal(expected, LogoContentSniffer.Sniff(head));

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Sniff_NotAnAllowedImage_ReturnsNull(byte[] head) =>
        Assert.Null(LogoContentSniffer.Sniff(head));
}
