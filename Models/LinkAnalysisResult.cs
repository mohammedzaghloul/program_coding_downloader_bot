namespace PremiumDownloader.Models;

public sealed class LinkAnalysisResult
{
    public string RequestedUrl { get; set; } = string.Empty;

    public string? ResolvedUrl { get; set; }

    public bool IsValidUrl { get; set; }

    public bool IsReachable { get; set; }

    public bool IsDirectFile { get; set; }

    public string? FileName { get; set; }

    public string? ContentType { get; set; }

    public long? ContentLength { get; set; }

    public string? UserMessage { get; set; }
}
