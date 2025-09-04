using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using SystemFileSystemWatcher = System.IO.FileSystemWatcher;

namespace Thaum.Core.Services;

/// <summary>
/// Handles workspace file system changes for LSP integration
/// </summary>
public class LSPWatcher : IDisposable {
	private readonly ILogger<LSPWatcher>                          _logger;
	private readonly ConcurrentDictionary<string, SystemFileSystemWatcher> _watchers                = new();
	private readonly ConcurrentQueue<SymbolChange>                         _changeQueue             = new();
	private readonly CancellationTokenSource                               _cancellationTokenSource = new();

	public LSPWatcher(ILogger<LSPWatcher> logger) {
		_logger = logger;
	}

	public void StartWatching(string workspacePath) {
		if (_watchers.ContainsKey(workspacePath)) {
			_logger.LogWarning("Already watching workspace: {WorkspacePath}", workspacePath);
			return;
		}

		try {
			SystemFileSystemWatcher watcher = new SystemFileSystemWatcher(workspacePath) {
				IncludeSubdirectories = true,
				NotifyFilter          = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
				Filter                = "*.*"
			};

			watcher.Created += (sender, e) => OnFileChanged(e, ChangeType.Added);
			watcher.Changed += (sender, e) => OnFileChanged(e, ChangeType.Modified);
			watcher.Deleted += (sender, e) => OnFileChanged(e, ChangeType.Deleted);
			watcher.Renamed += (sender, e) => OnFileRenamed(e);

			watcher.EnableRaisingEvents = true;
			_watchers[workspacePath]    = watcher;

			_logger.LogInformation("Started watching workspace: {WorkspacePath}", workspacePath);
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to start watching workspace: {WorkspacePath}", workspacePath);
		}
	}

	public void StopWatching(string workspacePath) {
		if (_watchers.TryRemove(workspacePath, out SystemFileSystemWatcher? watcher)) {
			watcher.EnableRaisingEvents = false;
			watcher.Dispose();
			_logger.LogInformation("Stopped watching workspace: {WorkspacePath}", workspacePath);
		}
	}

	public async IAsyncEnumerable<SymbolChange> GetChanges() {
		while (!_cancellationTokenSource.Token.IsCancellationRequested) {
			if (_changeQueue.TryDequeue(out SymbolChange? change)) {
				yield return change;
			} else {
				await Task.Delay(100, _cancellationTokenSource.Token);
			}
		}
	}

	private void OnFileChanged(FileSystemEventArgs e, ChangeType changeType) {
		// Filter for source code files
		if (!IsSourceFile(e.FullPath)) {
			return;
		}

		_logger.LogDebug("File {ChangeType}: {FilePath}", changeType, e.FullPath);

		SymbolChange change = new SymbolChange(
			FilePath: e.FullPath,
			Type: changeType
		);

		_changeQueue.Enqueue(change);
	}

	private void OnFileRenamed(RenamedEventArgs e) {
		if (!IsSourceFile(e.FullPath)) {
			return;
		}

		_logger.LogDebug("File Renamed: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);

		SymbolChange change = new SymbolChange(
			FilePath: e.FullPath,
			Type: ChangeType.Renamed
		);

		_changeQueue.Enqueue(change);
	}

	private static bool IsSourceFile(string filePath) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		return extension switch {
			".cs"   => true,
			".py"   => true,
			".js"   => true,
			".jsx"  => true,
			".ts"   => true,
			".tsx"  => true,
			".rs"   => true,
			".go"   => true,
			".cpp"  => true,
			".hpp"  => true,
			".h"    => true,
			".c"    => true,
			".java" => true,
			_       => false
		};
	}

	public void Dispose() {
		_cancellationTokenSource.Cancel();

		foreach (SystemFileSystemWatcher watcher in _watchers.Values) {
			watcher.EnableRaisingEvents = false;
			watcher.Dispose();
		}

		_watchers.Clear();
		_cancellationTokenSource.Dispose();
	}
}