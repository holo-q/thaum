using Microsoft.Extensions.Logging;
using Thaum.Core.Models;

namespace Thaum.Core.Services;

public abstract record McpSearchOptions(
	SymbolKind[]? SymbolKinds         = null,
	bool          IncludeUnsummarized = false,
	int           MaxResults          = 50,
	string[]?     FilePatterns        = null
);

public record McpSummarizationOptions(
	bool    ForceRefresh        = false,
	int     MaxDepth            = 3,
	bool    IncludeDependencies = true,
	string? LlmModel            = null
);

public abstract class McpRequestEventArgs : EventArgs {
	public string   RequestId  { get; init; } = string.Empty;
	public string   Method     { get; init; } = string.Empty;
	public object?  Parameters { get; init; }
	public DateTime Timestamp  { get; init; } = DateTime.UtcNow;
}

// Simplified MCP server placeholder until MCP package compatibility is resolved
public class MCPServer : IDisposable {
	private readonly Compressor         _compressor;
	private readonly Crawler        _crawler;
	private readonly ICache             _cache;
	private readonly ILogger<MCPServer> _logger;

	private bool _isRunning;

	public bool   IsRunning => _isRunning;
	public int    Port      { get; private set; }
	public string ServerUri => "stdio://thaum-mcp-server";

	public event EventHandler<McpRequestEventArgs>? RequestReceived;

	public MCPServer(
		Compressor         compressor,
		Crawler        crawler,
		ICache             cache,
		ILogger<MCPServer> logger) {
		_compressor         = compressor;
		_crawler = crawler;
		_cache              = cache;
		_logger             = logger;
	}

	public async Task StartAsync(int port = 0) {
		if (_isRunning) {
			_logger.LogWarning("MCP server is already running");
			return;
		}

		_logger.LogInformation("Simple MCP server placeholder started");
		_isRunning = true;
		Port       = port;

		await Task.CompletedTask;
	}

	public async Task StopAsync() {
		if (!_isRunning) {
			return;
		}

		_logger.LogInformation("Simple MCP server stopped");
		_isRunning = false;

		await Task.CompletedTask;
	}

	public async Task<string> SummarizeCodebaseAsync(string projectPath, string language, McpSummarizationOptions? options = null) {
		options ??= new McpSummarizationOptions();

		if (options.ForceRefresh) {
			await _cache.InvalidatePatternAsync($"*{projectPath}*");
		}

		SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(projectPath, language);

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

	public async Task<List<CodeSymbol>> SearchSymbolsAsync(string projectPath, string query, McpSearchOptions? options = null) {
		SymbolHierarchy? hierarchy = await GetHierarchyAsync(projectPath);
		if (hierarchy == null) {
			return new List<CodeSymbol>();
		}

		// Simple search implementation
		List<CodeSymbol> allSymbols = GetAllSymbolsFlat(hierarchy.RootSymbols);
		return allSymbols
			.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
			            (s.Summary?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
			.Take(options?.MaxResults ?? 50)
			.ToList();
	}

	public async Task<string> GetSymbolSummaryAsync(string projectPath, string symbolName, string filePath) {
		SymbolHierarchy? hierarchy = await GetHierarchyAsync(projectPath);
		if (hierarchy == null) {
			return "No hierarchy found. Run summarize_codebase first.";
		}

		CodeSymbol? symbol = FindSymbolByNameAndPath(hierarchy.RootSymbols, symbolName, filePath);
		if (symbol == null) {
			return $"Symbol '{symbolName}' not found in {filePath}";
		}

		return symbol.Summary ?? "No summary available";
	}

	public async Task<List<string>> GetExtractedKeysAsync(string projectPath) {
		SymbolHierarchy? hierarchy = await GetHierarchyAsync(projectPath);
		return hierarchy?.ExtractedKeys.Values.ToList() ?? new List<string>();
	}

	public async Task<SymbolHierarchy?> GetHierarchyAsync(string projectPath) {
		string cacheKey = $"hierarchy_{projectPath}";
		return await _cache.GetAsync<SymbolHierarchy>(cacheKey);
	}

	public async Task InvalidateCacheAsync(string projectPath, string? pattern = null) {
		string cachePattern = pattern ?? $"*{projectPath}*";
		await _cache.InvalidatePatternAsync(cachePattern);
	}

	private static List<CodeSymbol> GetAllSymbolsFlat(List<CodeSymbol> symbols) {
		List<CodeSymbol> result = new List<CodeSymbol>();
		foreach (CodeSymbol symbol in symbols) {
			result.Add(symbol);
			if (symbol.Children?.Any() == true) {
				result.AddRange(GetAllSymbolsFlat(symbol.Children));
			}
		}
		return result;
	}

	private static CodeSymbol? FindSymbolByNameAndPath(List<CodeSymbol> symbols, string name, string filePath) {
		foreach (CodeSymbol symbol in symbols) {
			if (symbol.Name == name && symbol.FilePath == filePath) {
				return symbol;
			}

			if (symbol.Children?.Any() == true) {
				CodeSymbol? found = FindSymbolByNameAndPath(symbol.Children, name, filePath);
				if (found != null) {
					return found;
				}
			}
		}
		return null;
	}

	public void Dispose() {
		StopAsync().Wait();
	}
}