using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PremiumDownloader.Models;

namespace PremiumDownloader.Services;

public sealed class RemoteFileService
{
    private static readonly HashSet<string> DirectExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mp3", ".m4a", ".wav", ".zip", ".rar", ".7z", ".pdf",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".txt", ".csv",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".apk", ".exe",
        ".msi", ".dmg", ".tar", ".gz"
    };

    private static readonly HashSet<string> DirectMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/zip",
        "application/octet-stream",
        "application/x-zip-compressed",
        "application/x-rar-compressed",
        "application/x-7z-compressed",
        "application/gzip",
        "application/x-gzip",
        "application/x-tar",
        "application/vnd.android.package-archive",
        "text/plain"
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<RemoteFileService> _logger;

    public RemoteFileService(HttpClient httpClient, ILogger<RemoteFileService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<LinkAnalysisResult> AnalyzeAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!TryCreateHttpUri(url, out var uri))
        {
            return new LinkAnalysisResult
            {
                RequestedUrl = url ?? string.Empty,
                UserMessage = "أرسل رابطًا صحيحًا يبدأ بـ http أو https.",
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new LinkAnalysisResult
                {
                    RequestedUrl = uri.ToString(),
                    ResolvedUrl = response.RequestMessage?.RequestUri?.ToString(),
                    IsValidUrl = true,
                    UserMessage = $"تعذر الوصول إلى الرابط الحالي. الخادم أعاد الحالة {(int)response.StatusCode}.",
                };
            }

            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            var contentDisposition = response.Content.Headers.ContentDisposition;
            var contentType = response.Content.Headers.ContentType?.MediaType;
            var fileName = ResolveFileName(finalUri, contentDisposition);
            var isDirectFile = IsDirectFile(finalUri, contentType, contentDisposition);

            return new LinkAnalysisResult
            {
                RequestedUrl = uri.ToString(),
                ResolvedUrl = finalUri.ToString(),
                IsValidUrl = true,
                IsReachable = true,
                IsDirectFile = isDirectFile,
                FileName = fileName,
                ContentType = contentType,
                ContentLength = response.Content.Headers.ContentLength,
                UserMessage = isDirectFile ? null : "الرابط لا يبدو ملفًا مباشرًا قابلًا للإرسال الفوري.",
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect remote link {Url}", url);

            return new LinkAnalysisResult
            {
                RequestedUrl = uri.ToString(),
                IsValidUrl = true,
                UserMessage = "تعذر فحص الرابط حاليًا. جرّب مرة أخرى أو افتحه من المصدر الأصلي.",
            };
        }
    }

    public async Task<RemoteFileStream> OpenReadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!TryCreateHttpUri(url, out var uri))
        {
            throw new InvalidOperationException("أرسل رابطًا صحيحًا يبدأ بـ http أو https.");
        }

        var response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, uri),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new InvalidOperationException($"تعذر تنزيل الملف من الرابط الحالي. الخادم أعاد الحالة {statusCode}.");
        }

        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var fileName = ResolveFileName(finalUri, contentDisposition);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        return new RemoteFileStream(response, stream, fileName, contentType, response.Content.Headers.ContentLength);
    }

    private static bool TryCreateHttpUri(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri!) &&
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsDirectFile(Uri uri, string? contentType, ContentDispositionHeaderValue? contentDisposition)
    {
        if (contentDisposition?.DispositionType?.Equals("attachment", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(extension) && DirectExtensions.Contains(extension))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ||
            contentType.StartsWith("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return DirectMimeTypes.Contains(contentType);
    }

    private static string ResolveFileName(Uri uri, ContentDispositionHeaderValue? contentDisposition)
    {
        var fileName = contentDisposition?.FileNameStar ??
                       contentDisposition?.FileName?.Trim('"');

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        var nameFromPath = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(nameFromPath) ? "download.bin" : nameFromPath;
    }
}
