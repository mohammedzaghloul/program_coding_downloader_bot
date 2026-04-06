namespace PremiumDownloader.Models;

public sealed class DownloadPageViewModel
{
    public string Url { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public string? InfoMessage { get; set; }

    public string? SourceUrl { get; set; }

    public string? FileName { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? FileSizeLabel => FileSizeBytes is long size ? FormatFileSize(size) : null;

    public static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}
