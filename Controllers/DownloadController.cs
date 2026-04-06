using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PremiumDownloader.Models;
using PremiumDownloader.Services;

namespace PremiumDownloader.Controllers;

public class DownloadController : Controller
{
    private readonly ILogger<DownloadController> _logger;
    private readonly RemoteFileService _remoteFileService;
    private readonly DownloaderOptions _options;

    public DownloadController(
        ILogger<DownloadController> logger,
        RemoteFileService remoteFileService,
        IOptions<DownloaderOptions> options)
    {
        _logger = logger;
        _remoteFileService = remoteFileService;
        _options = options.Value;
    }

    [HttpGet]
    public IActionResult Index(string? url)
    {
        return View("Workspace", new DownloadPageViewModel
        {
            Url = url?.Trim() ?? string.Empty,
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessDownload(DownloadPageViewModel model, CancellationToken cancellationToken)
    {
        model.Url = model.Url?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(model.Url))
        {
            model.ErrorMessage = "أدخل الرابط أولًا ثم أعد المحاولة.";
            return View("Workspace", model);
        }

        var analysis = await _remoteFileService.AnalyzeAsync(model.Url, cancellationToken);
        model.FileName = analysis.FileName;
        model.FileSizeBytes = analysis.ContentLength;
        model.SourceUrl = analysis.ResolvedUrl ?? model.Url;

        if (!analysis.IsValidUrl)
        {
            model.ErrorMessage = analysis.UserMessage;
            return View("Workspace", model);
        }

        if (!analysis.IsReachable)
        {
            model.ErrorMessage = analysis.UserMessage;
            return View("Workspace", model);
        }

        if (!analysis.IsDirectFile)
        {
            model.InfoMessage = "الرابط ليس ملفًا مباشرًا. افتح الصفحة الأصلية أو أرسله للبوت ليحوّلك إلى هذه الصفحة.";
            return View("Workspace", model);
        }

        if (analysis.ContentLength is long knownSize && knownSize > _options.MaxWebDownloadSizeBytes)
        {
            model.InfoMessage = $"الملف أكبر من الحد المتاح عبر الموقع ({DownloadPageViewModel.FormatFileSize(_options.MaxWebDownloadSizeBytes)}). استخدم الرابط الأصلي بدل التحميل المباشر.";
            return View("Workspace", model);
        }

        try
        {
            var remoteFile = await _remoteFileService.OpenReadAsync(model.Url, cancellationToken);

            if (remoteFile.ContentLength is long actualSize && actualSize > _options.MaxWebDownloadSizeBytes)
            {
                await remoteFile.DisposeAsync();
                model.InfoMessage = $"الملف أكبر من الحد المتاح عبر الموقع ({DownloadPageViewModel.FormatFileSize(_options.MaxWebDownloadSizeBytes)}). استخدم الرابط الأصلي بدل التحميل المباشر.";
                return View("Workspace", model);
            }

            HttpContext.Response.RegisterForDispose(remoteFile);

            return File(
                remoteFile.Stream,
                remoteFile.ContentType,
                remoteFile.FileName,
                enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing download for {Url}", model.Url);
            model.ErrorMessage = "حدث خطأ أثناء محاولة تنزيل الملف. جرّب مرة أخرى أو افتح المصدر الأصلي.";
            return View("Workspace", model);
        }
    }
}
