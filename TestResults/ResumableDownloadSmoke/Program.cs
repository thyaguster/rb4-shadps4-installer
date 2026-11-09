using System.Net;
using System.Net.Http.Headers;
using RB4ShadPS4Installer.Services;

var root = Path.Combine(Path.GetTempPath(), "rb4-resume-smoke-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    var payload = new byte[360_000];
    new Random(42).NextBytes(payload);
    const int interruptedAt = 90_000;
    using var handler = new InterruptingHandler(payload, interruptedAt);
    using var client = new HttpClient(handler);
    var progress = new RecordingProgress();
    var destination = Path.Combine(root, "archive.zip");

    await ResumableDownloadService.DownloadAsync(
        client, "https://downloads.invalid/archive.zip", destination, progress, CancellationToken.None);

    Assert(File.ReadAllBytes(destination).SequenceEqual(payload), "the resumed file must exactly match the payload");
    Assert(!File.Exists(destination + ".part"), "the partial file must be moved away after completion");
    Assert(handler.RequestCount == 2, "one interrupted request and one resumed request were expected");
    Assert(handler.ResumedFrom == interruptedAt, "the retry must request the exact number of retained bytes");
    Assert(progress.Values.Any(value => value.Status.StartsWith("Download interrupted", StringComparison.Ordinal)),
        "retry progress should be reported");
    Assert(progress.Values.Any(value => value.Resumed), "resumed transfer progress should be reported");
    Console.WriteLine("Resumable-download smoke tests passed.");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class RecordingProgress : IProgress<DownloadTransferProgress>
{
    public List<DownloadTransferProgress> Values { get; } = [];
    public void Report(DownloadTransferProgress value) => Values.Add(value);
}

sealed class InterruptingHandler(byte[] payload, int interruptedAt) : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public long? ResumedFrom { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (RequestCount == 1)
        {
            Assert(request.Headers.Range is null, "the first request must not contain a Range header");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new InterruptingStream(payload, interruptedAt))
            };
            response.Content.Headers.ContentLength = payload.Length;
            return Task.FromResult(response);
        }

        ResumedFrom = request.Headers.Range?.Ranges.Single().From;
        Assert(ResumedFrom is not null, "the retry must contain a Range header");
        var start = checked((int)(ResumedFrom ?? throw new Exception("The resume offset is missing.")));
        var remaining = new MemoryStream(payload, start, payload.Length - start, writable: false);
        var resumed = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new StreamContent(remaining)
        };
        resumed.Content.Headers.ContentLength = payload.Length - start;
        resumed.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, payload.Length - 1, payload.Length);
        return Task.FromResult(resumed);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

sealed class InterruptingStream(byte[] payload, int interruptedAt) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => payload.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= interruptedAt) throw new IOException("Simulated connection interruption");
        var available = Math.Min(count, interruptedAt - _position);
        Array.Copy(payload, _position, buffer, offset, available);
        _position += available;
        return available;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_position >= interruptedAt) return ValueTask.FromException<int>(new IOException("Simulated connection interruption"));
        var available = Math.Min(buffer.Length, interruptedAt - _position);
        payload.AsMemory(_position, available).CopyTo(buffer);
        _position += available;
        return ValueTask.FromResult(available);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
