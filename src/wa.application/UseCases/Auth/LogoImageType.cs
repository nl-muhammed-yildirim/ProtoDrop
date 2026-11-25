namespace wa.application.UseCases.Auth;

/// <summary>US-021-04 (FR-021-5): the image kinds an org logo may be, decided from content only.</summary>
public enum LogoImageType
{
    /// <summary>Portable Network Graphics (8-byte PNG signature).</summary>
    Png,
    /// <summary>JPEG image (FF D8 FF signature).</summary>
    Jpeg,
    /// <summary>WebP image (RIFF container with a WEBP tag).</summary>
    WebP,
    /// <summary>SVG vector image (an <c>svg</c> root element, optionally after an XML prolog or comments).</summary>
    Svg
}
