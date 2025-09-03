using Thaum.Core.Models;

namespace Thaum.Core.Services;

public interface IChangeDetectionService : IDisposable
{
    Task StartWatchingAsync(string projectPath, string language);
    Task StopWatchingAsync(string projectPath);
    IAsyncEnumerable<FileChangeEvent> GetChangeEventsAsync(string projectPath);
    Task<List<CodeSymbol>> GetAffectedSymbolsAsync(string filePath, ChangeType changeType);
    Task<List<string>> GetDependentFilesAsync(string filePath, string language);
    bool IsWatching(string projectPath);
}

public record FileChangeEvent(
    string FilePath,
    ChangeType ChangeType,
    DateTime Timestamp,
    List<CodeSymbol>? AffectedSymbols = null
);

public interface IDependencyTracker
{
    Task BuildDependencyGraphAsync(string projectPath, string language);
    Task UpdateDependencyAsync(string filePath, List<string> dependencies);
    Task<List<string>> GetDependentsAsync(string filePath);
    Task<List<string>> GetDependenciesAsync(string filePath);
    Task RemoveFileAsync(string filePath);
    void ClearGraph(string projectPath);
}