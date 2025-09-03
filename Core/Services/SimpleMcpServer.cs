using Microsoft.Extensions.Logging;
using Thaum.Core.Models;

namespace Thaum.Core.Services;

// Simplified MCP server placeholder until MCP package compatibility is resolved
public class SimpleMcpServer : IMcpServer, IMcpToolProvider
{
    private readonly ISummarizationEngine _summarizationEngine;
    private readonly ILspClientManager _lspManager;
    private readonly ICacheService _cache;
    private readonly ILogger<SimpleMcpServer> _logger;
    
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public int Port { get; private set; }
    public string ServerUri => "stdio://thaum-mcp-server";

    public event EventHandler<McpRequestEventArgs>? RequestReceived;

    public SimpleMcpServer(
        ISummarizationEngine summarizationEngine,
        ILspClientManager lspManager,
        ICacheService cache,
        ILogger<SimpleMcpServer> logger)
    {
        _summarizationEngine = summarizationEngine;
        _lspManager = lspManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task StartAsync(int port = 0)
    {
        if (_isRunning)
        {
            _logger.LogWarning("MCP server is already running");
            return;
        }

        _logger.LogInformation("Simple MCP server placeholder started");
        _isRunning = true;
        Port = port;
        
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _logger.LogInformation("Simple MCP server stopped");
        _isRunning = false;
        
        await Task.CompletedTask;
    }

    public async Task<string> SummarizeCodebaseAsync(string projectPath, string language, McpSummarizationOptions? options = null)
    {
        options ??= new McpSummarizationOptions();

        if (options.ForceRefresh)
        {
            await _cache.InvalidatePatternAsync($"*{projectPath}*");
        }

        var hierarchy = await _summarizationEngine.ProcessCodebaseAsync(projectPath, language);
        
        return $"""
            Codebase Summarization Complete
            ==============================
            
            Project: {Path.GetFileName(projectPath)}
            Language: {language}
            Root Symbols: {hierarchy.RootSymbols.Count}
            Extracted Keys: {hierarchy.ExtractedKeys.Count}
            Last Updated: {hierarchy.LastUpdated}
            """;
    }

    public async Task<List<CodeSymbol>> SearchSymbolsAsync(string projectPath, string query, McpSearchOptions? options = null)
    {
        var hierarchy = await GetHierarchyAsync(projectPath);
        if (hierarchy == null)
        {
            return new List<CodeSymbol>();
        }

        // Simple search implementation
        var allSymbols = GetAllSymbolsFlat(hierarchy.RootSymbols);
        return allSymbols
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || 
                       (s.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(options?.MaxResults ?? 50)
            .ToList();
    }

    public async Task<string> GetSymbolSummaryAsync(string projectPath, string symbolName, string filePath)
    {
        var hierarchy = await GetHierarchyAsync(projectPath);
        if (hierarchy == null)
        {
            return "No hierarchy found. Run summarize_codebase first.";
        }

        var symbol = FindSymbolByNameAndPath(hierarchy.RootSymbols, symbolName, filePath);
        if (symbol == null)
        {
            return $"Symbol '{symbolName}' not found in {filePath}";
        }

        return symbol.Summary ?? "No summary available";
    }

    public async Task<List<string>> GetExtractedKeysAsync(string projectPath)
    {
        var hierarchy = await GetHierarchyAsync(projectPath);
        return hierarchy?.ExtractedKeys.Values.ToList() ?? new List<string>();
    }

    public async Task<SymbolHierarchy?> GetHierarchyAsync(string projectPath)
    {
        var cacheKey = $"hierarchy_{projectPath}";
        return await _cache.GetAsync<SymbolHierarchy>(cacheKey);
    }

    public async Task InvalidateCacheAsync(string projectPath, string? pattern = null)
    {
        var cachePattern = pattern ?? $"*{projectPath}*";
        await _cache.InvalidatePatternAsync(cachePattern);
    }

    private static List<CodeSymbol> GetAllSymbolsFlat(List<CodeSymbol> symbols)
    {
        var result = new List<CodeSymbol>();
        foreach (var symbol in symbols)
        {
            result.Add(symbol);
            if (symbol.Children?.Any() == true)
            {
                result.AddRange(GetAllSymbolsFlat(symbol.Children));
            }
        }
        return result;
    }

    private static CodeSymbol? FindSymbolByNameAndPath(List<CodeSymbol> symbols, string name, string filePath)
    {
        foreach (var symbol in symbols)
        {
            if (symbol.Name == name && symbol.FilePath == filePath)
            {
                return symbol;
            }
            
            if (symbol.Children?.Any() == true)
            {
                var found = FindSymbolByNameAndPath(symbol.Children, name, filePath);
                if (found != null)
                {
                    return found;
                }
            }
        }
        return null;
    }

    public void Dispose()
    {
        StopAsync().Wait();
    }
}