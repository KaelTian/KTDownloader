using System.Net;
using System.Net.Http.Headers;
using KT.Downloader.Cli.Downloading;

namespace KT.Downloader.Cli.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        => _respond = respond;

    public static StubHttpMessageHandler Returns(HttpStatusCode statusCode, HttpContent content)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(statusCode) { Content = content }));

    public static StubHttpMessageHandler Throws(Exception exception)
        => new((_, _) => Task.FromException<HttpResponseMessage>(exception));

    /// <summary>一个会按 Range 请求返回 206 的假服务器；honorRange 设成 false 就退回 200 + 完整内容。</summary>
    public static StubHttpMessageHandler ServingRangeAware(byte[] content, bool honorRange = true)
    {
        return new StubHttpMessageHandler((request, _) =>
        {
            var requested = honorRange ? request.Headers.Range?.Ranges.FirstOrDefault() : null;
            var offset = requested?.From ?? 0;

            if (offset <= 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content),
                });
            }

            if (offset >= content.Length)
            {
                var notSatisfiable = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    Content = new ByteArrayContent([]),
                };
                notSatisfiable.Content.Headers.ContentRange = new ContentRangeHeaderValue(content.Length);

                return Task.FromResult(notSatisfiable);
            }

            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(content[(int)offset..]),
            };
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, content.Length - 1, content.Length);

            return Task.FromResult(partial);
        });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _respond(request, cancellationToken);
}

internal sealed class GatedStream : Stream
{
    private readonly byte[] _data;
    private readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _position;

    public GatedStream(byte[] data) => _data = data;

    public Task FirstRead => _firstRead.Task;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _data.Length)
        {
            var count = Math.Min(buffer.Length, _data.Length - _position);
            _data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            _firstRead.TrySetResult();
            return count;
        }

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("测试用的流只支持异步读取。");

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class StubFileDownloader : IFileDownloader
{
    private readonly DownloadResult _result;

    public StubFileDownloader(DownloadResult result) => _result = result;

    public DownloadRequest? LastRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public IProgress<DownloadProgress>? LastProgress { get; private set; }

    public Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress>? progress = null)
    {
        LastRequest = request;
        LastCancellationToken = cancellationToken;
        LastProgress = progress;
        return Task.FromResult(_result);
    }
}

internal sealed class ChunkThenFailStream : Stream
{
    private readonly byte[] _data;
    private int _position;

    public ChunkThenFailStream(byte[] data) => _data = data;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _data.Length)
            throw new IOException("连接被重置。");

        var count = Math.Min(buffer.Length, _data.Length - _position);
        _data.AsSpan(_position, count).CopyTo(buffer.Span);
        _position += count;

        return ValueTask.FromResult(count);
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("测试用的流只支持异步读取。");

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class CollectingProgress : IProgress<DownloadProgress>
{
    private readonly List<DownloadProgress> _reports;

    public CollectingProgress(List<DownloadProgress> reports) => _reports = reports;

    public void Report(DownloadProgress value) => _reports.Add(value);
}

internal sealed class FakeTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + new TimeSpan(_timestamp);

    public void Advance(TimeSpan delta) => _timestamp += delta.Ticks;
}
