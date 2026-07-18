using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DivaModManager.Helpers
{
    /// <summary>
    /// Cross-platform HTTP download helper with progress reporting.
    /// Ported verbatim from the original DMM HttpClientExtensions.
    /// </summary>
    public static class HttpClientExtensions
    {
        public static long GetDirectorySize(this DirectoryInfo directoryInfo, bool recursive = true)
        {
            var startDirectorySize = default(long);
            if (directoryInfo == null || !directoryInfo.Exists)
                return startDirectorySize;

            foreach (var fileInfo in directoryInfo.GetFiles())
                System.Threading.Interlocked.Add(ref startDirectorySize, fileInfo.Length);

            if (recursive)
                Parallel.ForEach(directoryInfo.GetDirectories(), (subDirectory) =>
                    System.Threading.Interlocked.Add(ref startDirectorySize, GetDirectorySize(subDirectory, recursive)));

            return startDirectorySize;
        }

        public static async Task DownloadAsync(this System.Net.Http.HttpClient client,
            string requestUri, Stream destination, string? fileName,
            IProgress<Models.DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using (var response = await client.GetAsync(requestUri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;

                using (var download = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    if (progress == null || !contentLength.HasValue)
                    {
                        await download.CopyToAsync(destination, cancellationToken);
                        return;
                    }

                    var relativeProgress = new Progress<long>(totalBytes =>
                        progress.Report(new Models.DownloadProgress(
                            (float)totalBytes / contentLength.Value,
                            totalBytes,
                            contentLength.Value,
                            fileName)));
                    await download.CopyToAsync(destination, 81920, relativeProgress, cancellationToken);
                    progress.Report(new Models.DownloadProgress(1, contentLength.Value, contentLength.Value, fileName));
                }
            }
        }
    }

    public static class StreamExtensions
    {
        public static async Task CopyToAsync(this Stream source, Stream destination, int bufferSize,
            IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (!source.CanRead)
                throw new ArgumentException("Has to be readable", nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (!destination.CanWrite)
                throw new ArgumentException("Has to be writable", nameof(destination));
            if (bufferSize < 0)
                throw new ArgumentOutOfRangeException(nameof(bufferSize));

            var buffer = new byte[bufferSize];
            long totalBytesRead = 0;
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalBytesRead += bytesRead;
                progress?.Report(totalBytesRead);
            }
        }
    }
}
