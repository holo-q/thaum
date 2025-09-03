using Thaum.Core.Models;

namespace Thaum.Core.Services;

public interface IMcpServer : IDisposable
{
    Task StartAsync(int port = 0);
    Task StopAsync();
    bool IsRunning { get; }
    int Port { get; }
    string ServerUri { get; }
    
    event EventHandler<McpRequestEventArgs>? RequestReceived;
}

public class McpRequestEventArgs : EventArgs
{
    public string RequestId { get; init; } = string.Empty;
    public string Method { get; init; } = string.Empty;
    public object? Parameters { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public interface IMcpToolProvider
{
    Task<string> SummarizeCodebaseAsync(string projectPath, string language, McpSummarizationOptions? options = null);
    Task<List<CodeSymbol>> SearchSymbolsAsync(string projectPath, string query, McpSearchOptions? options = null);
    Task<string> GetSymbolSummaryAsync(string projectPath, string symbolName, string filePath);
    Task<List<string>> GetExtractedKeysAsync(string projectPath);
    Task<SymbolHierarchy?> GetHierarchyAsync(string projectPath);
    Task InvalidateCacheAsync(string projectPath, string? pattern = null);
}

public record McpSummarizationOptions(
    bool ForceRefresh = false,
    int MaxDepth = 3,
    bool IncludeDependencies = true,
    string? LlmModel = null
);

public record McpSearchOptions(
    SymbolKind[]? SymbolKinds = null,
    bool IncludeUnsummarized = false,
    int MaxResults = 50,
    string[]? FilePatterns = null
);