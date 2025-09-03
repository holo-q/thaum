using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Thaum.Core.Services;

/// <summary>
/// Manages automatic downloading, installation, and versioning of LSP servers
/// </summary>
public class LspServerManager
{
    private readonly ILogger<LspServerManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ILspDownloadProgress? _progressReporter;
    
    public LspServerManager(ILogger<LspServerManager> logger, HttpClient? httpClient = null, ILspDownloadProgress? progressReporter = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _progressReporter = progressReporter;
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Thaum", 
            "lsp-servers"
        );
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <summary>
    /// Get the path to an LSP server executable, downloading if necessary
    /// </summary>
    public async Task<string?> GetLspServerPathAsync(string language)
    {
        _logger.LogDebug("Getting LSP server path for {Language}", language);
        
        var serverInfo = GetServerInfo(language);
        if (serverInfo == null)
        {
            _logger.LogWarning("No server configuration found for language: {Language}", language);
            return null;
        }

        var serverDir = Path.Combine(_cacheDirectory, language);
        var executablePath = Path.Combine(serverDir, serverInfo.ExecutableName);



        // Check if we already have the server
        if (File.Exists(executablePath))
        {
            _logger.LogDebug("Using cached LSP server: {Path}", executablePath);
            return executablePath;
        }

        // Download and install the server
        _logger.LogInformation("Downloading LSP server for {Language}...", language);
        _progressReporter?.ReportProgress(serverInfo.Name, 0, "Starting download...");
        
        var success = await DownloadAndInstallServerAsync(language, serverInfo);
        
        if (success && File.Exists(executablePath))
        {
            _logger.LogInformation("Successfully installed LSP server for {Language}", language);
            _progressReporter?.ReportComplete(serverInfo.Name, true);
            return executablePath;
        }

        _logger.LogError("Failed to install LSP server for {Language}", language);
        _progressReporter?.ReportComplete(serverInfo.Name, false);
        return null;
    }

    /// <summary>
    /// Get OmniSharp server info with correct platform-specific URLs
    /// </summary>
    private static LspServerInfo GetOmniSharpServerInfo()
    {
        string downloadUrl;
        string executableName;
        
        // Debug platform detection
        Console.WriteLine($"DEBUG: Windows: {RuntimeInformation.IsOSPlatform(OSPlatform.Windows)}");
        Console.WriteLine($"DEBUG: Linux: {RuntimeInformation.IsOSPlatform(OSPlatform.Linux)}");
        Console.WriteLine($"DEBUG: OSX: {RuntimeInformation.IsOSPlatform(OSPlatform.OSX)}");
        Console.WriteLine($"DEBUG: OS: {RuntimeInformation.OSDescription}");
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            downloadUrl = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-win-x64.zip";
            executableName = "OmniSharp.exe";
            Console.WriteLine("DEBUG: Selected Windows");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            downloadUrl = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-osx-x64.tar.gz";
            executableName = "run";  // OSX also uses the run script
            Console.WriteLine("DEBUG: Selected OSX");
        }
        else // Linux
        {
            downloadUrl = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-linux-x64-net6.0.tar.gz";
            executableName = "OmniSharp";  // .NET 6.0 version has direct executable
            Console.WriteLine("DEBUG: Selected Linux (.NET 6.0)");
        }
        
        Console.WriteLine($"DEBUG: Final URL: {downloadUrl}");
        
        return new LspServerInfo
        {
            Name = "OmniSharp",
            Version = "1.39.14",
            ExecutableName = executableName,
            Arguments = new[] { "--languageserver" },
            DownloadUrl = downloadUrl
        };
    }

    /// <summary>
    /// Get server configuration for a language
    /// </summary>
    private static LspServerInfo? GetServerInfo(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "csharp" => GetOmniSharpServerInfo(),
            _ => null
        };
    }

    /// <summary>
    /// Download and install an LSP server
    /// </summary>
    private async Task<bool> DownloadAndInstallServerAsync(string language, LspServerInfo serverInfo)
    {
        try
        {
            var serverDir = Path.Combine(_cacheDirectory, language);
            
            // Clean up existing installation
            if (Directory.Exists(serverDir))
            {
                Directory.Delete(serverDir, true);
            }
            Directory.CreateDirectory(serverDir);

            var downloadUrl = serverInfo.DownloadUrl;
            _logger.LogDebug("Downloading from: {Url}", downloadUrl);

            // Download with progress reporting
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            var tempFile = Path.Combine(serverDir, "download" + GetFileExtension(downloadUrl));
            
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            
            _progressReporter?.ReportProgress(serverInfo.Name, 0, "Downloading...");
            
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(tempFile))
            {
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    
                    if (totalBytes.HasValue && totalBytes > 0)
                    {
                        var percent = (int)((totalRead * 100) / totalBytes.Value);
                        _progressReporter?.ReportProgress(serverInfo.Name, percent, $"Downloading... ({totalRead / 1024} KB)");
                    }
                }
                
                await fileStream.FlushAsync();
            }

            _logger.LogDebug("Downloaded {Bytes} bytes to {File}", totalRead, tempFile);

            // Extract
            _logger.LogDebug("Extracting {File} to {Dir}", tempFile, serverDir);
            if (IsArchive(tempFile))
            {
                await ExtractArchiveAsync(tempFile, serverDir);
                File.Delete(tempFile);
            }
            else
            {
                var targetPath = Path.Combine(serverDir, serverInfo.ExecutableName);
                File.Move(tempFile, targetPath);
            }

            // Find the actual executable (OmniSharp extracts to different locations)
            var executablePath = FindExecutableInDirectory(serverDir, serverInfo.ExecutableName);
            
            if (executablePath == null)
            {
                _logger.LogError("Could not find executable {Name} after extraction", serverInfo.ExecutableName);
                return false;
            }

            // Set executable permissions on Unix
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var chmod = new System.Diagnostics.ProcessStartInfo("chmod", $"+x \"{executablePath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var chmodProcess = System.Diagnostics.Process.Start(chmod);
                await chmodProcess!.WaitForExitAsync();
            }

            // Save version info
            await File.WriteAllTextAsync(Path.Combine(serverDir, ".version"), serverInfo.Version);

            return File.Exists(executablePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download and install LSP server for {Language}", language);
            return false;
        }
    }

    /// <summary>
    /// Find executable in extracted directory
    /// </summary>
    private string? FindExecutableInDirectory(string directory, string expectedName)
    {
        // First try the expected location
        var expectedPath = Path.Combine(directory, expectedName);
        if (File.Exists(expectedPath))
        {
            return expectedPath;
        }

        // For OmniSharp, try common variations
        var possibleNames = new[] { "OmniSharp", "omnisharp", "OmniSharp.exe", "run" };
        foreach (var name in possibleNames)
        {
            var testPath = Path.Combine(directory, name);
            if (File.Exists(testPath))
            {
                _logger.LogDebug("Found executable at: {Path}", testPath);
                return testPath;
            }

            // Also check subdirectories
            var subdirs = Directory.GetDirectories(directory);
            foreach (var subdir in subdirs)
            {
                var subdirPath = Path.Combine(subdir, name);
                if (File.Exists(subdirPath))
                {
                    _logger.LogDebug("Found executable in subdirectory: {Path}", subdirPath);
                    return subdirPath;
                }
            }
        }

        _logger.LogWarning("Could not find executable in {Directory}. Files: {Files}", 
            directory, string.Join(", ", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Take(10).Select(Path.GetFileName)));

        return null;
    }

    /// <summary>
    /// Get file extension from URL
    /// </summary>
    private static string GetFileExtension(string url)
    {
        var uri = new Uri(url);
        var fileName = Path.GetFileName(uri.LocalPath);
        
        // Handle .tar.gz specifically
        if (fileName.EndsWith(".tar.gz"))
        {
            return ".tar.gz";
        }
        
        return Path.GetExtension(fileName);
    }

    /// <summary>
    /// Check if file is an archive
    /// </summary>
    private static bool IsArchive(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension == ".zip" || extension == ".gz" || filePath.EndsWith(".tar.gz");
    }

    /// <summary>
    /// Extract archive to directory
    /// </summary>
    private async Task ExtractArchiveAsync(string archivePath, string extractPath)
    {
        if (archivePath.EndsWith(".zip"))
        {
            ZipFile.ExtractToDirectory(archivePath, extractPath);
        }
        else if (archivePath.EndsWith(".tar.gz"))
        {
            // Extract tar.gz using tar command
            var tar = new System.Diagnostics.ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{extractPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            using var tarProcess = System.Diagnostics.Process.Start(tar);
            if (tarProcess != null)
            {
                var stdout = await tarProcess.StandardOutput.ReadToEndAsync();
                var stderr = await tarProcess.StandardError.ReadToEndAsync();
                await tarProcess.WaitForExitAsync();
                
                _logger.LogDebug("Executing: tar -xzf \"{Archive}\" -C \"{Extract}\"", archivePath, extractPath);
                _logger.LogDebug("Tar stdout: {Output}", stdout);
                _logger.LogDebug("Tar stderr: {Error}", stderr);
                _logger.LogDebug("Tar exit code: {Code}", tarProcess.ExitCode);
                
                if (tarProcess.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Failed to extract tar.gz: {stderr}");
                }
            }
        }
    }

    /// <summary>
    /// Clean up old server installations
    /// </summary>
    public async Task CleanupOldServersAsync()
    {
        try
        {
            var dirs = Directory.GetDirectories(_cacheDirectory);
            foreach (var dir in dirs)
            {
                var versionFile = Path.Combine(dir, ".version");
                if (File.Exists(versionFile))
                {
                    var versionDate = File.GetLastWriteTime(versionFile);
                    if (DateTime.Now - versionDate > TimeSpan.FromDays(30))
                    {
                        _logger.LogInformation("Cleaning up old LSP server: {Dir}", dir);
                        Directory.Delete(dir, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old LSP servers");
        }
    }
}

/// <summary>
/// Information about an LSP server
/// </summary>
public record LspServerInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string ExecutableName { get; init; }
    public string[] Arguments { get; init; } = Array.Empty<string>();
    public required string DownloadUrl { get; init; }
}