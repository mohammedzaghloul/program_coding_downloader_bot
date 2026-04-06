using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PremiumDownloader.Models;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace PremiumDownloader.Services;

public class TelegramBotService : BackgroundService
{
    private readonly ILogger<TelegramBotService> _logger;
    private readonly RemoteFileService _remoteFileService;
    private readonly DownloaderOptions _options;
    private ITelegramBotClient? _botClient;

    public TelegramBotService(
        ILogger<TelegramBotService> logger,
        RemoteFileService remoteFileService,
        IOptions<DownloaderOptions> options)
    {
        _logger = logger;
        _remoteFileService = remoteFileService;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TelegramBotToken))
        {
            _logger.LogWarning("Telegram bot token is not configured. Bot startup skipped.");
            return;
        }

        _botClient = new TelegramBotClient(_options.TelegramBotToken);
        _logger.LogInformation("Telegram bot service is starting.");

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        _botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } messageText } message)
        {
            return;
        }

        var chatId = message.Chat.Id;
        var text = messageText.Trim();

        _logger.LogInformation("Received message in chat {ChatId}: {MessageText}", chatId, text);

        if (string.Equals(text, "/start", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(
                chatId,
                "أرسل رابطًا مباشرًا يبدأ بـ http أو https.\n\nإذا كان الرابط ملفًا مباشرًا سأرسله لك فورًا، وإذا لم يكن مباشرًا سأحولك إلى صفحة التحميل على موقعك.",
                cancellationToken: cancellationToken);
            return;
        }

        var analysis = await _remoteFileService.AnalyzeAsync(text, cancellationToken);

        if (!analysis.IsValidUrl)
        {
            await botClient.SendMessage(
                chatId,
                analysis.UserMessage ?? "أرسل رابطًا صحيحًا يبدأ بـ http أو https.",
                cancellationToken: cancellationToken);
            return;
        }

        if (!analysis.IsReachable)
        {
            await botClient.SendMessage(
                chatId,
                analysis.UserMessage ?? "تعذر الوصول إلى الرابط الحالي.",
                cancellationToken: cancellationToken);
            return;
        }

        if (!analysis.IsDirectFile)
        {
            await SendWebsiteFallbackAsync(botClient, chatId, text, cancellationToken);
            return;
        }

        if (analysis.ContentLength is long knownSize && knownSize > _options.MaxTelegramFileSizeBytes)
        {
            await botClient.SendMessage(
                chatId,
                $"الملف أكبر من الحد المناسب للإرسال داخل تيليجرام ({DownloadPageViewModel.FormatFileSize(_options.MaxTelegramFileSizeBytes)}).",
                cancellationToken: cancellationToken);
            await SendWebsiteFallbackAsync(botClient, chatId, text, cancellationToken);
            return;
        }

        try
        {
            await botClient.SendMessage(
                chatId,
                $"جارٍ تجهيز الملف: {analysis.FileName ?? "download"}",
                cancellationToken: cancellationToken);

            await using var remoteFile = await _remoteFileService.OpenReadAsync(text, cancellationToken);

            if (remoteFile.ContentLength is long actualSize && actualSize > _options.MaxTelegramFileSizeBytes)
            {
                await botClient.SendMessage(
                    chatId,
                    $"الملف أكبر من الحد المناسب للإرسال داخل تيليجرام ({DownloadPageViewModel.FormatFileSize(_options.MaxTelegramFileSizeBytes)}).",
                    cancellationToken: cancellationToken);
                await SendWebsiteFallbackAsync(botClient, chatId, text, cancellationToken);
                return;
            }

            await botClient.SendDocument(
                chatId,
                InputFile.FromStream(remoteFile.Stream, remoteFile.FileName),
                caption: "تم تجهيز الملف بنجاح.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing Telegram download for {Url}", text);
            await botClient.SendMessage(
                chatId,
                "حدث خطأ أثناء تجهيز الملف. جرّب الرابط مرة أخرى أو افتحه من الموقع.",
                cancellationToken: cancellationToken);
        }

        return;
    }

    private async Task SendWebsiteFallbackAsync(ITelegramBotClient botClient, long chatId, string originalUrl, CancellationToken cancellationToken)
    {
        var pageUrl = BuildWebsiteDownloadUrl(originalUrl);

        await botClient.SendMessage(
            chatId,
            "هذا الرابط ليس ملفًا مباشرًا أو أنه يحتاج معالجة خارج تيليجرام. افتح صفحة التحميل من الزر التالي.",
            replyMarkup: new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("فتح صفحة التحميل", pageUrl)),
            cancellationToken: cancellationToken);
    }

    private string BuildWebsiteDownloadUrl(string originalUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.WebsiteBaseUrl)
            ? "https://localhost:5001"
            : _options.WebsiteBaseUrl.TrimEnd('/');

        return $"{baseUrl}/Download?url={Uri.EscapeDataString(originalUrl)}";
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Telegram Bot error.");
        return Task.CompletedTask;
    }
}
