namespace DivaModManager.Models
{
    public class DownloadProgress
    {
        public DownloadProgress(float percentage, long downloadedBytes, long totalBytes, string? fileName = null)
        {
            Percentage = percentage;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            FileName = fileName;
        }
        public float Percentage { get; }
        public long DownloadedBytes { get; }
        public long TotalBytes { get; }
        public string? FileName { get; }
    }
}
