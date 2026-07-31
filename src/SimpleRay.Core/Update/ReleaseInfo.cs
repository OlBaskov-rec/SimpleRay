namespace SimpleRay.Core.Update;

/// <summary>A newer release discovered on GitHub, with the assets needed to update.</summary>
public sealed class ReleaseInfo
{
    public required Version Version { get; init; }
    public required string ZipUrl { get; init; }

    /// <summary>ECDSA signature (.sig) over the zip. Null on legacy releases that predate signing.</summary>
    public string? SigUrl { get; init; }

    /// <summary>SHA-256 sidecar (.sha256). Null once a release ships only a signature.</summary>
    public string? Sha256Url { get; init; }

    public string? Notes { get; init; }
    public string? HtmlUrl { get; init; }
}
