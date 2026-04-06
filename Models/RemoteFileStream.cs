using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace PremiumDownloader.Models;

public sealed class RemoteFileStream : IDisposable, IAsyncDisposable
{
    private readonly HttpResponseMessage _response;

    public RemoteFileStream(HttpResponseMessage response, Stream stream, string fileName, string contentType, long? contentLength)
    {
        _response = response;
        Stream = stream;
        FileName = fileName;
        ContentType = contentType;
        ContentLength = contentLength;
    }

    public Stream Stream { get; }

    public string FileName { get; }

    public string ContentType { get; }

    public long? ContentLength { get; }

    public void Dispose()
    {
        Stream.Dispose();
        _response.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        _response.Dispose();
    }
}
