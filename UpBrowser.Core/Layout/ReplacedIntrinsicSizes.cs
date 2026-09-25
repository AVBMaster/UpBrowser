using UpBrowser.Core.Layout.Geometry;

namespace UpBrowser.Core.Layout;

/// <summary>
/// Seam between layout and the image decoder: replaced elements need their
/// intrinsic size during layout, but decoding lives in the rendering layer.
/// The renderer registers <see cref="Resolver"/> with a cache-backed lookup.
/// </summary>
public static class ReplacedIntrinsicSizes
{
    /// <summary>Returns the decoded size for a raw (unresolved) source string, or
    /// null when the image is unknown — layout then falls back to the default size.</summary>
    public static Func<string?, PhysicalSize?>? Resolver;

    public static PhysicalSize? Lookup(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return null;
        try
        {
            return Resolver?.Invoke(source);
        }
        catch
        {
            return null;
        }
    }
}
