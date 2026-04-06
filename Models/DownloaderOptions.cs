namespace PremiumDownloader.Models;

public sealed class DownloaderOptions
{
    public const string SectionName = "Downloader";

    public string TelegramBotToken { get; set; } = string.Empty;

    public string TelegramBotUsername { get; set; } = "YourBot";

    public string WebsiteBaseUrl { get; set; } = "https://localhost:5001";

    public long MaxTelegramFileSizeBytes { get; set; } = 45L * 1024 * 1024;

    public long MaxWebDownloadSizeBytes { get; set; } = 200L * 1024 * 1024;
}
