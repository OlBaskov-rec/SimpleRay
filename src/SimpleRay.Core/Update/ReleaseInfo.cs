namespace SimpleRay.Core.Update;

/// <summary>A newer release discovered on GitHub, with the assets needed to update.</summary>
public sealed class ReleaseInfo
{
    public required Version Version { get; init; }
    public required string ZipUrl { get; init; }
    public required string Sha256Url { get; init; }
    public string? Notes { get; init; }
    public string? HtmlUrl { get; init; }
}
