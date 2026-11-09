using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace RB4ShadPS4Installer.Services;

internal static class ResumableDownloadService
{
    private const int MaximumAttempts = 3;
    private const int BufferSize = 131072;

    public static async Task DownloadAsync(
        HttpClient client,
        string url,
        string destination,
        IProgress<DownloadTransferProgress>? progress,
        CancellationToken token)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var partial = destination + ".part";

        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);
                using var response = await client.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, token);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
                    existing > 0 && response.Content.Headers.ContentRange?.Length == existing)
                {
                    File.Move(partial, destination, overwrite: true);
                    progress?.Report(new(existing, existing, attempt, true, "Download complete"));
                    return;
                }

                response.EnsureSuccessStatusCode();
                var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (resumed && response.Content.Headers.ContentRange?.From != existing)
                    throw new InvalidDataException("The download server returned an invalid resume range.");
                if (!resumed) existing = 0;

                var total = response.Content.Headers.ContentRange?.Length ??
                    (response.Content.Headers.ContentLength is long length ? existing + length : null);
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(
                    partial,
                    resumed ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    useAsync: true);
                var buffer = new byte[BufferSize];
                var received = existing;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    received += read;
                    progress?.Report(new(received, total, attempt, resumed,
                        resumed ? "Resuming download" : "Downloading"));
                }
                await output.FlushAsync(token);
                if (total is long expected && received != expected)
                    throw new EndOfStreamException($"The download ended at {received:N0} of {expected:N0} bytes.");

                output.Close();
                File.Move(partial, destination, overwrite: true);
                progress?.Report(new(received, total, attempt, resumed, "Download complete"));
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                if (attempt >= MaximumAttempts)
                    throw new IOException("The download could not be completed after three attempts.", ex);
                var received = File.Exists(partial) ? new FileInfo(partial).Length : 0;
                progress?.Report(new(received, null, attempt + 1, received > 0,
                    $"Download interrupted; retrying ({attempt + 1}/{MaximumAttempts})"));
                await Task.Delay(TimeSpan.FromSeconds(attempt), token);
            }
        }

        throw new IOException("The download could not be completed after three attempts.");
    }
}

internal sealed record DownloadTransferProgress(
    long BytesReceived,
    long? TotalBytes,
    int Attempt,
    bool Resumed,
    string Status);
