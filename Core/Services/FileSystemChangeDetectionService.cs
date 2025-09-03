using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Thaum.Core.Models;

namespace Thaum.Core.Services;

public class FileSystemChangeDetectionService : IChangeDetectionService
{
    private readonly ILspClientManager _lspManager;
    private readonly IDependencyTracker _dependencyTracker;
    private readonly ILogger<FileSystemChangeDetectionService> _logger;
    private readonly ConcurrentDictionary<string, ProjectWatcher> _watchers = new();

    public FileSystemChangeDetectionService(
        ILspClientManager lspManager,
        IDependencyTracker dependencyTracker,
        ILogger<FileSystemChangeDetectionService> logger)
    {
        _lspManager = lspManager;
        _dependencyTracker = dependencyTracker;
        _logger = logger;
    }

    public async Task StartWatchingAsync(string projectPath, string language)
    {
        if (_watchers.ContainsKey(projectPath))
        {
            _logger.LogInformation("Already watching project {ProjectPath}", projectPath);
            return;
        }

        try
        {
            var watcher = new ProjectWatcher(projectPath, language, _lspManager, _dependencyTracker, _logger);
            _watchers[projectPath] = watcher;
            
            await watcher.StartAsync();
            _logger.LogInformation("Started watching project {ProjectPath} for {Language}", projectPath, language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watching project {ProjectPath}", projectPath);
            throw;
        }
    }

    public async Task StopWatchingAsync(string projectPath)
    {
        if (_watchers.TryRemove(projectPath, out var watcher))
        {
            await watcher.StopAsync();
            _logger.LogInformation("Stopped watching project {ProjectPath}", projectPath);
        }
    }

    public async IAsyncEnumerable<FileChangeEvent> GetChangeEventsAsync(string projectPath)
    {
        if (!_watchers.TryGetValue(projectPath, out var watcher))
        {
            yield break;
        }

        await foreach (var changeEvent in watcher.GetChangesAsync())
        {
            yield return changeEvent;
        }
    }

    public async Task<List<CodeSymbol>> GetAffectedSymbolsAsync(string filePath, ChangeType changeType)
    {
        try
        {
            var projectPath = FindProjectPathForFile(filePath);
            if (projectPath == null || !_watchers.TryGetValue(projectPath, out var watcher))
            {
                return new List<CodeSymbol>();
            }

            return await watcher.GetAffectedSymbolsAsync(filePath, changeType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting affected symbols for {FilePath}", filePath);
            return new List<CodeSymbol>();
        }
    }

    public async Task<List<string>> GetDependentFilesAsync(string filePath, string language)
    {
        try
        {
            var dependents = await _dependencyTracker.GetDependentsAsync(filePath);
            return dependents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dependent files for {FilePath}", filePath);
            return new List<string>();
        }
    }

    public bool IsWatching(string projectPath)
    {
        return _watchers.ContainsKey(projectPath);
    }

    private string? FindProjectPathForFile(string filePath)
    {
        return _watchers.Keys.FirstOrDefault(projectPath => filePath.StartsWith(projectPath));
    }

    public void Dispose()
    {
        foreach (var (_, watcher) in _watchers)
        {
            watcher.StopAsync().Wait();
        }
        _watchers.Clear();
    }
}

internal class ProjectWatcher : IDisposable
{
    private readonly string _projectPath;
    private readonly string _language;
    private readonly ILspClientManager _lspManager;
    private readonly IDependencyTracker _dependencyTracker;
    private readonly ILogger _logger;
    private readonly FileSystemWatcher _fileWatcher;
    private readonly ConcurrentQueue<FileChangeEvent> _changeQueue = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public ProjectWatcher(
        string projectPath,
        string language,
        ILspClientManager lspManager,
        IDependencyTracker dependencyTracker,
        ILogger logger)
    {
        _projectPath = projectPath;
        _language = language;
        _lspManager = lspManager;
        _dependencyTracker = dependencyTracker;
        _logger = logger;

        _fileWatcher = new FileSystemWatcher(projectPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Created += OnFileCreated;
        _fileWatcher.Deleted += OnFileDeleted;
        _fileWatcher.Renamed += OnFileRenamed;
    }

    public async Task StartAsync()
    {
        // Build initial dependency graph
        await _dependencyTracker.BuildDependencyGraphAsync(_projectPath, _language);
        
        _fileWatcher.EnableRaisingEvents = true;
    }

    public async Task StopAsync()
    {
        _fileWatcher.EnableRaisingEvents = false;
        _cancellationTokenSource.Cancel();
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<FileChangeEvent> GetChangesAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            if (_changeQueue.TryDequeue(out var changeEvent))
            {
                yield return changeEvent;
            }
            else
            {
                await Task.Delay(100, _cancellationTokenSource.Token);
            }
        }
    }

    public async Task<List<CodeSymbol>> GetAffectedSymbolsAsync(string filePath, ChangeType changeType)
    {
        try
        {
            if (changeType == ChangeType.Deleted)
            {
                return new List<CodeSymbol>();
            }

            return await _lspManager.GetDocumentSymbolsAsync(_language, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting affected symbols for {FilePath}", filePath);
            return new List<CodeSymbol>();
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsSourceFile(e.FullPath))
        {
            EnqueueChange(e.FullPath, ChangeType.Modified);
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        if (IsSourceFile(e.FullPath))
        {
            EnqueueChange(e.FullPath, ChangeType.Added);
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (IsSourceFile(e.FullPath))
        {
            EnqueueChange(e.FullPath, ChangeType.Deleted);
            
            // Clean up dependency tracking
            Task.Run(async () =>
            {
                try
                {
                    await _dependencyTracker.RemoveFileAsync(e.FullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error removing file from dependency tracker: {FilePath}", e.FullPath);
                }
            });
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsSourceFile(e.FullPath))
        {
            // Treat rename as delete old + create new
            EnqueueChange(e.OldFullPath, ChangeType.Deleted);
            EnqueueChange(e.FullPath, ChangeType.Added);
            
            Task.Run(async () =>
            {
                try
                {
                    await _dependencyTracker.RemoveFileAsync(e.OldFullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling file rename in dependency tracker: {OldPath} -> {NewPath}", 
                        e.OldFullPath, e.FullPath);
                }
            });
        }
    }

    private void EnqueueChange(string filePath, ChangeType changeType)
    {
        Task.Run(async () =>
        {
            try
            {
                var affectedSymbols = await GetAffectedSymbolsAsync(filePath, changeType);
                
                var changeEvent = new FileChangeEvent(
                    FilePath: filePath,
                    ChangeType: changeType,
                    Timestamp: DateTime.UtcNow,
                    AffectedSymbols: affectedSymbols
                );
                
                _changeQueue.Enqueue(changeEvent);
                
                _logger.LogDebug("Detected {ChangeType} in {FilePath} affecting {SymbolCount} symbols", 
                    changeType, filePath, affectedSymbols.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file change: {FilePath}", filePath);
            }
        });
    }

    private static bool IsSourceFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension is ".py" or ".cs" or ".js" or ".ts" or ".rs" or ".go" or ".java" or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp";
    }

    public void Dispose()
    {
        _fileWatcher?.Dispose();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}