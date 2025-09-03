namespace Thaum.Core.Services;

/// <summary>
/// Progress reporting interface for LSP server downloads
/// </summary>
public interface ILspDownloadProgress
{
    /// <summary>
    /// Report download progress
    /// </summary>
    /// <param name="serverName">Name of the server being downloaded</param>
    /// <param name="progressPercent">Progress percentage (0-100)</param>
    /// <param name="message">Status message</param>
    void ReportProgress(string serverName, int progressPercent, string message);

    /// <summary>
    /// Report download completion
    /// </summary>
    /// <param name="serverName">Name of the server</param>
    /// <param name="success">Whether the download was successful</param>
    void ReportComplete(string serverName, bool success);
}

/// <summary>
/// Console-based progress reporter
/// </summary>
public class ConsoleDownloadProgress : ILspDownloadProgress
{
    private readonly object _lock = new();
    private int _lastPercent = -1;

    public void ReportProgress(string serverName, int progressPercent, string message)
    {
        lock (_lock)
        {
            // Only update if percentage changed significantly
            if (Math.Abs(progressPercent - _lastPercent) >= 5 || _lastPercent == -1)
            {
                Console.Write($"\r🔽 {serverName}: {progressPercent:D3}% - {message}");
                _lastPercent = progressPercent;
            }
        }
    }

    public void ReportComplete(string serverName, bool success)
    {
        lock (_lock)
        {
            if (success)
            {
                Console.WriteLine($"\r✅ {serverName}: Download complete!                    ");
            }
            else
            {
                Console.WriteLine($"\r❌ {serverName}: Download failed!                      ");
            }
            _lastPercent = -1;
        }
    }
}