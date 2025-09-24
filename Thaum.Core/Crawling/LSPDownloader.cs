using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Ratatui;
using Thaum.Core.Utils;

namespace Thaum.Core.Crawling;

/// <summary>
/// Manages automatic downloading, installation, and versioning of LSP servers
/// </summary>
[LoggingIntrinsics]
public partial class LSPDownloader {
	private readonly ILogger<LSPDownloader>   _logger;
	private readonly HttpClient               _http;
	private readonly string                   _cachedir;
	private readonly ConsoleDownloadProgress? _progressReporter;

	public LSPDownloader(HttpClient? httpClient = null, ConsoleDownloadProgress? progressReporter = null) {
		_logger           = RatLog.Get<LSPDownloader>();
		_http             = httpClient ?? new HttpClient();
		_progressReporter = progressReporter;
		_cachedir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Thaum",
			"lsp-servers"
		);
		Directory.CreateDirectory(_cachedir);
	}

	/// <summary>
	/// Get the path to an LSP server executable, downloading if necessary
	/// </summary>
	public async Task<string?> GetLspServerPathAsync(string language) {
		trace("Getting LSP server path for {Language}", language);

		LspServerInfo? serverInfo = GetServerInfo(language);
		if (serverInfo == null) {
			warn("No server configuration found for language: {Language}", language);
			return null;
		}

		string serverDir      = Path.Combine(_cachedir, language);
		string executablePath = Path.Combine(serverDir, serverInfo.ExecutableName);


		// Check if we already have the server
		if (File.Exists(executablePath)) {
			trace("Using cached LSP server: {Path}", executablePath);
			return executablePath;
		}

		// Download and install the server
		info("Downloading LSP server for {Language}...", language);
		_progressReporter?.ReportProgress(serverInfo.Name, 0, "Starting download...");

		bool success = await DownloadAndInstallServerAsync(language, serverInfo);

		if (success && File.Exists(executablePath)) {
			info("Successfully installed LSP server for {Language}", language);
			_progressReporter?.ReportComplete(serverInfo.Name, true);
			return executablePath;
		}

		err("Failed to install LSP server for {Language}", language);
		_progressReporter?.ReportComplete(serverInfo.Name, false);
		return null;
	}

	/// <summary>
	/// Get OmniSharp server info with correct platform-specific URLs
	/// </summary>
	private static LspServerInfo GetOmniSharpServerInfo() {
		string downloadUrl;
		string executableName;

		// Debug platform detection
		Tracer.println($"DEBUG: Windows: {RuntimeInformation.IsOSPlatform(OSPlatform.Windows)}");
		Tracer.println($"DEBUG: Linux: {RuntimeInformation.IsOSPlatform(OSPlatform.Linux)}");
		Tracer.println($"DEBUG: OSX: {RuntimeInformation.IsOSPlatform(OSPlatform.OSX)}");
		Tracer.println($"DEBUG: OS: {RuntimeInformation.OSDescription}");

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			downloadUrl    = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-win-x64.zip";
			executableName = "OmniSharp.exe";
			Tracer.println("DEBUG: Selected Windows");
		} else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
			downloadUrl    = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-osx-x64.tar.gz";
			executableName = "run"; // OSX also uses the run script
			Tracer.println("DEBUG: Selected OSX");
		} else // Linux
		{
			downloadUrl    = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-linux-x64-net6.0.tar.gz";
			executableName = "OmniSharp"; // .NET 6.0 version has direct executable
			Tracer.println("DEBUG: Selected Linux (.NET 6.0)");
		}

		Tracer.println($"DEBUG: Final URL: {downloadUrl}");

		return new LspServerInfo {
			Name           = "OmniSharp",
			Version        = "1.39.14",
			ExecutableName = executableName,
			Arguments      = ["--languageserver"],
			DownloadUrl    = downloadUrl
		};
	}

	/// <summary>
	/// Get server configuration for a language
	/// </summary>
	private static LspServerInfo? GetServerInfo(string language) {
		return language.ToLowerInvariant() switch {
			"c-sharp" => GetOmniSharpServerInfo(),
			_         => null
		};
	}

	/// <summary>
	/// Download and install an LSP server
	/// </summary>
	private async Task<bool> DownloadAndInstallServerAsync(string language, LspServerInfo serverInfo) {
		try {
			string serverDir = Path.Combine(_cachedir, language);

			// Clean up existing installation
			if (Directory.Exists(serverDir)) {
				Directory.Delete(serverDir, true);
			}
			Directory.CreateDirectory(serverDir);

			string downloadUrl = serverInfo.DownloadUrl;
			trace("Downloading from: {Url}", downloadUrl);

			// Download with progress reporting
			using HttpResponseMessage response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
			response.EnsureSuccessStatusCode();

			long?  totalBytes = response.Content.Headers.ContentLength;
			string tempFile   = Path.Combine(serverDir, $"download{GetFileExtension(downloadUrl)}");

			byte[] buffer    = new byte[GLB.FileBufferSize];
			long   totalRead = 0;
			int    bytesRead;

			_progressReporter?.ReportProgress(serverInfo.Name, 0, "Downloading...");

			await using (Stream contentStream = await response.Content.ReadAsStreamAsync())
			await using (FileStream fileStream = File.Create(tempFile)) {
				while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0) {
					await fileStream.WriteAsync(buffer, 0, bytesRead);
					totalRead += bytesRead;

					if (totalBytes is > 0) {
						int percent = (int)((totalRead * 100) / totalBytes.Value);
						_progressReporter?.ReportProgress(serverInfo.Name, percent, $"Downloading... ({totalRead / 1024} KB)");
					}
				}

				await fileStream.FlushAsync();
			}

			trace("Downloaded {Bytes} bytes to {File}", totalRead, tempFile);

			// Extract
			trace("Extracting {File} to {Dir}", tempFile, serverDir);
			if (IsArchive(tempFile)) {
				await ExtractArchiveAsync(tempFile, serverDir);
				File.Delete(tempFile);
			} else {
				string targetPath = Path.Combine(serverDir, serverInfo.ExecutableName);
				File.Move(tempFile, targetPath);
			}

			// Find the actual executable (OmniSharp extracts to different locations)
			string? executablePath = FindExecutableInDirectory(serverDir, serverInfo.ExecutableName);

			if (executablePath == null) {
				err("Could not find executable {Name} after extraction", serverInfo.ExecutableName);
				return false;
			}

			// Set executable permissions on Unix
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				ProcessStartInfo chmod = new ProcessStartInfo("chmod", $"+x \"{executablePath}\"") {
					UseShellExecute = false,
					CreateNoWindow  = true
				};
				using Process? chmodProcess = Process.Start(chmod);
				await chmodProcess!.WaitForExitAsync();
			}

			// Save version info
			await File.WriteAllTextAsync(Path.Combine(serverDir, ".version"), serverInfo.Version);

			return File.Exists(executablePath);
		} catch (Exception ex) {
			err(ex, "Failed to download and install LSP server for {Language}", language);
			return false;
		}
	}

	/// <summary>
	/// Find executable in extracted directory
	/// </summary>
	private string? FindExecutableInDirectory(string directory, string expectedName) {
		// First try the expected location
		string expectedPath = Path.Combine(directory, expectedName);
		if (File.Exists(expectedPath)) {
			return expectedPath;
		}

		// For OmniSharp, try common variations
		string[] possibleNames = ["OmniSharp", "omnisharp", "OmniSharp.exe", "run"];
		foreach (string name in possibleNames) {
			string testPath = Path.Combine(directory, name);
			if (File.Exists(testPath)) {
				trace("Found executable at: {Path}", testPath);
				return testPath;
			}

			// Also check subdirectories
			string[] subdirs = Directory.GetDirectories(directory);
			foreach (string subdir in subdirs) {
				string subdirPath = Path.Combine(subdir, name);
				if (File.Exists(subdirPath)) {
					trace("Found executable in subdirectory: {Path}", subdirPath);
					return subdirPath;
				}
			}
		}

		warn("Could not find executable in {Directory}. Files: {Files}",
			directory, string.Join(", ", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Take(10).Select(Path.GetFileName)));

		return null;
	}

	/// <summary>
	/// Get file extension from URL
	/// </summary>
	private static string GetFileExtension(string url) {
		Uri    uri      = new Uri(url);
		string fileName = Path.GetFileName(uri.LocalPath);

		// Handle .tar.gz specifically
		return fileName.EndsWith(".tar.gz")
			? ".tar.gz"
			: Path.GetExtension(fileName);
	}

	/// <summary>
	/// Check if file is an archive
	/// </summary>
	private static bool IsArchive(string filePath) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		return extension == ".zip" || extension == ".gz" || filePath.EndsWith(".tar.gz");
	}

	/// <summary>
	/// Extract archive to directory
	/// </summary>
	private async Task ExtractArchiveAsync(string archivePath, string extractPath) {
		if (archivePath.EndsWith(".zip")) {
			ZipFile.ExtractToDirectory(archivePath, extractPath);
		} else if (archivePath.EndsWith(".tar.gz")) {
			// Extract tar.gz using tar command
			ProcessStartInfo tar = new ProcessStartInfo("tar", $"-xzf \"{archivePath}\" -C \"{extractPath}\"") {
				UseShellExecute        = false,
				CreateNoWindow         = true,
				RedirectStandardOutput = true,
				RedirectStandardError  = true
			};

			using Process? tarProcess = Process.Start(tar);
			if (tarProcess != null) {
				string stdout = await tarProcess.StandardOutput.ReadToEndAsync();
				string stderr = await tarProcess.StandardError.ReadToEndAsync();
				await tarProcess.WaitForExitAsync();

				_logger.LogDebug("Executing: tar -xzf \"{Archive}\" -C \"{Extract}\"", archivePath, extractPath);
				_logger.LogDebug("Tar stdout: {Output}", stdout);
				_logger.LogDebug("Tar stderr: {Error}", stderr);
				_logger.LogDebug("Tar exit code: {Code}", tarProcess.ExitCode);

				if (tarProcess.ExitCode != 0) {
					throw new InvalidOperationException($"Failed to extract tar.gz: {stderr}");
				}
			}
		}
	}

	/// <summary>
	/// Clean up old server installations
	/// </summary>
	public async Task CleanupOldServersAsync() {
		try {
			string[] dirs = Directory.GetDirectories(_cachedir);
			foreach (string dir in dirs) {
				string versionFile = Path.Combine(dir, ".version");
				if (File.Exists(versionFile)) {
					DateTime versionDate = File.GetLastWriteTime(versionFile);
					if (DateTime.Now - versionDate > TimeSpan.FromDays(30)) {
						info("Cleaning up old LSP server: {Dir}", dir);
						Directory.Delete(dir, true);
					}
				}
			}
		} catch (Exception ex) {
			err(ex, "Failed to cleanup old LSP servers");
		}
	}

	/// <summary>
	/// Console-based progress reporter
	/// TODO I feel like there is a library we're missing like rich for python...
	/// </summary>
	public class ConsoleDownloadProgress {
		private readonly Lock _lock = new();
		private          int  _last = -1;

		public void ReportProgress(string name, int percent, string message) {
			lock (_lock) {
				// Only update if percentage changed significantly
				if (Math.Abs(percent - _last) >= 5 || _last == -1) {
					Console.Write($"\r🔽 {name}: {percent:D3}% - {message}");
					_last = percent;
				}
			}
		}

		public void ReportComplete(string name, bool success) {
			lock (_lock) {
				if (success) {
					Tracer.println($"\r✅ {name}: Download complete!                    ");
				} else {
					Tracer.println($"\r❌ {name}: Download failed!                      ");
				}
				_last = -1;
			}
		}
	}
}

/// <summary>
/// Information about an LSP server
/// </summary>
public record LspServerInfo {
	public required string   Name           { get; init; }
	public required string   Version        { get; init; }
	public required string   ExecutableName { get; init; }
	public          string[] Arguments      { get; init; } = [];
	public required string   DownloadUrl    { get; init; }
}