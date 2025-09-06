using Microsoft.Extensions.Logging;
using System.Text;
using Thaum.Core.Models;
using Thaum.Utils;
using static Thaum.Core.Utils.TraceLogger;

namespace Thaum.Core.Services;

/// <summary>
/// Context for hierarchical compression where Level indicates depth (1=function 2=class) where
/// AvailableKeys contains discovered semantic patterns from previous phases where each level
/// builds on previous discoveries creating progressive refinement of understanding
/// </summary>
public record OptimizationContext(
	int              Level,
	List<string>     AvailableKeys,
	CompressionLevel CompressionLevel = CompressionLevel.Optimize,
	string?          ParentContext    = null,
	List<string>?    SiblingContexts  = null
) {
	private async Task<string> BuildCustomPrompt(string promptName, CodeSymbol symbol, string sourceCode) {
		return await new PromptLoader().FormatPrompt(promptName, new Dictionary<string, object> {
			["sourceCode"] = sourceCode,
			["symbolName"] = symbol.Name,
			["availableKeys"] = this.AvailableKeys.Any()
				? string.Join("\n", this.AvailableKeys.Select(k => $"- {k}"))
				: "NONE"
		});
	}
}

/// <summary>
/// Orchestrates hierarchical codebase compression through multi-phase analysis where functions
/// compress first revealing K1 patterns where classes compress with K1 revealing K2 patterns
/// where final pass uses K1+K2 achieving maximum semantic density where caching prevents
/// redundant LLM calls where streaming enables real-time progress visualization
/// </summary>
public class Compressor {
	private readonly LLM                 _llm;
	private readonly CodeCrawler         _codeCrawlerManager;
	private readonly ICache              _cache;
	private readonly PromptLoader        _promptLoader;
	private readonly ILogger<Compressor> _logger;

	public Compressor(
		LLM          llm,
		CodeCrawler  crawler,
		ICache       cache,
		PromptLoader promptLoader) {
		_llm                = llm;
		_codeCrawlerManager = crawler;
		_cache              = cache;
		_promptLoader       = promptLoader;
		_logger             = Logging.For<Compressor>();
	}

	/// <summary>
	/// Compresses single symbol where caching checks prevent redundant work where prompt
	/// construction uses discovered keys where LLM temperature 0.3 balances consistency
	/// with creativity where result caching enables incremental processing
	/// </summary>
	public async Task<string> OptimizeSymbolAsync(CodeSymbol symbol, OptimizationContext context, string code) {
		string cacheKey = $"optimization_{symbol.Name}_{symbol.FilePath}_{symbol.StartCodeLoc.Line}_{context.Level}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			return cached;
		}

		string prompt = await BuildOptimizationPromptAsync(symbol, context, code);

		// Get the prompt name used for this optimization
		string compressionPrefix = context.CompressionLevel.GetPromptPrefix();
		string symbolType = symbol.Kind switch {
			SymbolKind.Function or SymbolKind.Method => "function",
			SymbolKind.Class                         => "class",
			_                                        => "function"
		};

		string envVarName = $"THAUM_PROMPT_{compressionPrefix.ToUpper()}_{symbolType.ToUpper()}";
		string promptName = Environment.GetEnvironmentVariable(envVarName) ?? GetDefaultPromptName(compressionPrefix, symbolType);

		// Get the raw prompt template content
		string promptContent = await _promptLoader.LoadPrompt(promptName);

		// Get model from configuration - fail fast if not available
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		string summary = await _llm.CompleteAsync(prompt, new LLMOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));

		// Determine provider from model name (simple heuristic)
		string provider = model.Contains("gpt") ? "openai" :
			model.Contains("claude")            ? "anthropic" :
			model.Contains("llama")             ? "ollama" : "unknown";

		await _cache.SetAsync(cacheKey, summary, TimeSpan.FromHours(24), promptName, promptContent, model, provider);
		return summary;
	}

	/// <summary>
	/// Extracts semantic patterns from multiple compressions where K1 emerges from functions
	/// where K2 emerges from classes where pattern recognition uses lower temperature for
	/// consistency where discovered keys enable higher-level compressions
	/// </summary>
	public async Task<string> ExtractCommonKeyAsync(List<string> summaries, int level, CompressionLevel compressionLevel = CompressionLevel.Optimize) {
		string cacheKey = $"key_L{level}_{GetOptimizationsHash(summaries)}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			return cached;
		}

		string prompt = await BuildKeyExtractionPromptAsync(summaries, level, compressionLevel);

		// Get prompt name and content for metadata
		string compressionPrefix = compressionLevel.GetPromptPrefix();
		string promptName        = $"{compressionPrefix}_key";
		string promptContent     = await _promptLoader.LoadPrompt(promptName);

		// Get model from configuration - fail fast if not available
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		string key = await _llm.CompleteAsync(prompt, new LLMOptions(Temperature: 0.2, MaxTokens: 512, Model: model));

		// Determine provider from model name (simple heuristic)
		string provider = model.Contains("gpt") ? "openai" :
			model.Contains("claude")            ? "anthropic" :
			model.Contains("llama")             ? "ollama" : "unknown";

		await _cache.SetAsync(cacheKey, key, TimeSpan.FromDays(1), promptName, promptContent, model, provider);
		return key;
	}

	/// <summary>
	/// Six-phase hierarchical compression pipeline where Phase1=function analysis where
	/// Phase2=K1 extraction where Phase3=function reanalysis with K1 where Phase4=class
	/// analysis with K1 where Phase5=K2 extraction where Phase6=final reanalysis with K1+K2
	/// where parallel processing maximizes throughput where streaming enables progress tracking
	/// </summary>
	public async Task<SymbolHierarchy> ProcessCodebaseAsync(string projectPath, string language, CompressionLevel compressionLevel = CompressionLevel.Optimize) {
		traceheader("HIERARCHICAL CODEBASE ANALYSIS");
		_logger.LogInformation("Starting codebase processing for {ProjectPath}", projectPath);

		traceln("LSP Server", $"{language.ToUpper()}", "INIT");

		traceln("Workspace", "Symbol Discovery", "SCAN");
		List<CodeSymbol> allSymbols       = await _codeCrawlerManager.CrawlDir(projectPath);
		HierarchyBuilder hierarchyBuilder = new HierarchyBuilder();

		// Phase 1: Optimize functions (deepest scope)
		traceheader("PHASE 1: FUNCTION ANALYSIS");
		List<CodeSymbol> functions = allSymbols.Where(s => s.Kind is SymbolKind.Function or SymbolKind.Method).ToList();

		traceln("Parallel Processing", $"{functions.Count} functions", "START");

		IEnumerable<Task<string>> functionOptimizationTasks = functions.Select(async function => {
			traceln(function.Name, "LLM Analysis", "STREAM");
			string              sourceCode = await GetSymbolSourceCode(function);
			OptimizationContext context    = new OptimizationContext(Level: 1, AvailableKeys: [], CompressionLevel: compressionLevel);
			return await OptimizeSymbolWithStreamAsync(function, context, sourceCode);
		});

		List<string> functionOptimizations = (await Task.WhenAll(functionOptimizationTasks)).ToList();

		// Phase 2: Extract K1 from function summaries
		traceheader("PHASE 2: K1 EXTRACTION");
		traceln("Function Optimizations", "Pattern Analysis", "KEY");
		string                     k1            = await ExtractCommonKeyWithStreamAsync(functionOptimizations, 1, compressionLevel);
		Dictionary<string, string> extractedKeys = new Dictionary<string, string> { ["K1"] = k1 };
		traceln("K1 Extracted", k1.Length > 50 ? $"{k1[..47]}..." : k1, "DONE");

		// Phase 3: Re-summarize functions with K1
		traceheader("PHASE 3: FUNCTION RE-ANALYSIS WITH K1");
		traceln("Parallel Re-analysis", $"{functions.Count} functions", "START");

		IEnumerable<Task<string>> functionReanalysisTasks = functions.Select(async function => {
			traceln(function.Name, "K1-Enhanced Analysis", "STREAM");
			string              sourceCode = await GetSymbolSourceCode(function);
			OptimizationContext context    = new OptimizationContext(Level: 1, AvailableKeys: [k1], CompressionLevel: compressionLevel);
			return await OptimizeSymbolWithStreamAsync(function, context, sourceCode);
		});

		await Task.WhenAll(functionReanalysisTasks);

		// Phase 4: Optimize classes with K1
		traceheader("PHASE 4: CLASS ANALYSIS WITH K1");
		List<CodeSymbol> classes = allSymbols.Where(s => s.Kind == SymbolKind.Class).ToList();

		traceln("Parallel Processing", $"{classes.Count} classes", "START");

		IEnumerable<Task<string>> classOptimizationTasks = classes.Select(async cls => {
			traceln(cls.Name, "K1-Enhanced Analysis", "STREAM");
			string              sourceCode = await GetSymbolSourceCode(cls);
			OptimizationContext context    = new OptimizationContext(Level: 2, AvailableKeys: [k1], CompressionLevel: compressionLevel);
			return await OptimizeSymbolWithStreamAsync(cls, context, sourceCode);
		});

		List<string> classOptimizations = (await Task.WhenAll(classOptimizationTasks)).ToList();

		// Phase 5: Extract K2 from class summaries
		traceheader("PHASE 5: K2 EXTRACTION");
		traceln("Class Optimizations", "Pattern Analysis", "KEY");
		string k2 = await ExtractCommonKeyWithStreamAsync(classOptimizations, 2, compressionLevel);
		extractedKeys["K2"] = k2;
		traceln("K2 Extracted", k2.Length > 50 ? $"{k2[..47]}..." : k2, "DONE");

		// Phase 6: Re-summarize everything with K1+K2
		traceheader("PHASE 6: FINAL RE-ANALYSIS WITH K1+K2");
		await ResummarizeWithKeysAsync(functions.Concat(classes).ToList(), [k1, k2], compressionLevel);

		traceheader("HIERARCHY CONSTRUCTION");
		traceln("Flat Symbols", "Nested Structure", "BUILD");
		List<CodeSymbol> rootSymbols = hierarchyBuilder.BuildHierarchy(allSymbols);

		traceln("Analysis Complete", $"{allSymbols.Count} symbols processed", "DONE");
		return new SymbolHierarchy(projectPath, rootSymbols, extractedKeys, DateTime.UtcNow);
	}

	public async Task<SymbolHierarchy> UpdateHierarchyAsync(SymbolHierarchy existing, List<CodeChange> changes) {
		// Implement incremental update logic
		foreach (CodeChange change in changes) {
			await InvalidateCacheForChange(change);
		}

		// Re-process only affected symbols and their dependencies
		return existing; // Placeholder - implement incremental logic
	}

	public async Task<string> GetOptimizedPromptAsync(string basePrompt, List<string> examples, string task) {
		string optimizationPrompt = $"""
		                             You are an expert prompt optimizer. Given the base prompt and examples below, 
		                             optimize the prompt for the specific task: {task}

		                             Base Prompt:
		                             {basePrompt}

		                             Examples:
		                             {string.Join("\n\n", examples)}

		                             Provide an optimized version that will produce better results for this task.
		                             Focus on clarity, specificity, and effectiveness.
		                             """;

		return await _llm.CompleteAsync(optimizationPrompt, new LLMOptions(Temperature: 0.3));
	}

	private async Task<string> BuildOptimizationPromptAsync(CodeSymbol symbol, OptimizationContext context, string sourceCode) {
		string compressionPrefix = context.CompressionLevel.GetPromptPrefix();
		string symbolType = symbol.Kind switch {
			SymbolKind.Function or SymbolKind.Method => "function",
			SymbolKind.Class                         => "class",
			_                                        => "function"
		};

		// Allow environment variable override for prompt names
		string envVarName = $"THAUM_PROMPT_{compressionPrefix.ToUpper()}_{symbolType.ToUpper()}";
		string promptName = Environment.GetEnvironmentVariable(envVarName) ?? GetDefaultPromptName(compressionPrefix, symbolType);

		Dictionary<string, object> parameters = new Dictionary<string, object> {
			["sourceCode"] = sourceCode,
			["symbolName"] = symbol.Name,
			["availableKeys"] = context.AvailableKeys.Any()
				? string.Join("\n", context.AvailableKeys.Select(k => $"- {k}"))
				: "None"
		};

		return await _promptLoader.FormatPrompt(promptName, parameters);
	}

	private string GetDefaultPromptName(string compressionPrefix, string symbolType) {
		// Use compress_function_v2 as the default for compress functions
		if (compressionPrefix == "compress" && symbolType == "function") {
			return "compress_function_v2";
		}

		// Default pattern for all other cases
		return $"{compressionPrefix}_{symbolType}";
	}

	private async Task<string> BuildKeyExtractionPromptAsync(List<string> summaries, int level, CompressionLevel compressionLevel = CompressionLevel.Optimize) {
		string compressionPrefix = compressionLevel.GetPromptPrefix();
		string promptName        = $"{compressionPrefix}_key";

		Dictionary<string, object> parameters = new Dictionary<string, object> {
			["summaries"] = string.Join("\n", summaries.Select((s, i) => $"{i + 1}. {s}")),
			["level"]     = level.ToString()
		};

		return await _promptLoader.FormatPrompt(promptName, parameters);
	}

	private async Task<string> GetSymbolSourceCode(CodeSymbol symbol) {
		try {
			string[] lines     = await File.ReadAllLinesAsync(symbol.FilePath);
			int      startLine = Math.Max(0, symbol.StartCodeLoc.Line);
			int      endLine   = Math.Min(lines.Length - 1, symbol.EndCodeLoc.Line);

			return string.Join("\n", lines[startLine..(endLine + 1)]);
		} catch (Exception ex) {
			_logger.LogError(ex, "Failed to read source code for symbol {SymbolName}", symbol.Name);
			return "";
		}
	}

	/// <summary>
	/// Streaming compression where tokens appear in real-time enabling progress visualization
	/// where cache hit bypasses LLM call where streaming accumulates full response for caching
	/// where trace output shows compression progress and result size
	/// </summary>
	private async Task<string> OptimizeSymbolWithStreamAsync(CodeSymbol symbol, OptimizationContext context, string sourceCode) {
		string cacheKey = $"optimization_{symbol.Name}_{symbol.FilePath}_{symbol.StartCodeLoc.Line}_{context.Level}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			traceln(symbol.Name, "Cached Optimization", "HIT");
			return cached;
		}

		string prompt = await BuildOptimizationPromptAsync(symbol, context, sourceCode);

		// Get model from configuration - fail fast if not available
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		// Stream the response in real-time
		IAsyncEnumerable<string> streamResponse = await _llm.StreamCompleteAsync(prompt, new LLMOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));
		StringBuilder            summary        = new StringBuilder();

		await foreach (string token in streamResponse) {
			summary.Append(token);
			// Real-time display would go here - for now just continue collecting
		}

		string result = summary.ToString().Trim();
		await _cache.SetAsync(cacheKey, result, TimeSpan.FromHours(24));
		traceln(symbol.Name, $"Optimization ({result.Length} chars)", "DONE");

		return result;
	}

	private async Task<string> ExtractCommonKeyWithStreamAsync(List<string> summaries, int level, CompressionLevel compressionLevel = CompressionLevel.Optimize) {
		string cacheKey = $"key_L{level}_{GetOptimizationsHash(summaries)}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			traceln($"K{level} Extraction", "Cached Key", "HIT");
			return cached;
		}

		string prompt = await BuildKeyExtractionPromptAsync(summaries, level, compressionLevel);

		// Get model from configuration - fail fast if not available
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		// Stream the key extraction response
		IAsyncEnumerable<string> streamResponse = await _llm.StreamCompleteAsync(prompt, new LLMOptions(Temperature: 0.2, MaxTokens: 512, Model: model));
		StringBuilder            key            = new StringBuilder();

		await foreach (string token in streamResponse) {
			key.Append(token);
			// Real-time display of key extraction
		}

		string result = key.ToString().Trim();
		await _cache.SetAsync(cacheKey, result, TimeSpan.FromDays(1));

		return result;
	}

	private async Task ResummarizeWithKeysAsync(List<CodeSymbol> symbols, List<string> keys, CompressionLevel compressionLevel) {
		traceln("Parallel Final Analysis", $"{symbols.Count} symbols", "START");

		IEnumerable<Task<string>> reanalysisTasks = symbols.Select(async symbol => {
			traceln(symbol.Name, "Final K1+K2 Analysis", "STREAM");
			string sourceCode = await GetSymbolSourceCode(symbol);
			OptimizationContext context = new OptimizationContext(
				Level: symbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
				AvailableKeys: keys,
				CompressionLevel: compressionLevel
			);
			return await OptimizeSymbolWithStreamAsync(symbol, context, sourceCode);
		});

		await Task.WhenAll(reanalysisTasks);
	}

	private async Task InvalidateCacheForChange(CodeChange change) {
		// Implement cache invalidation logic based on the change
		string pattern = $"optimization_{change.Symbol?.Name ?? "*"}_{change.FilePath}*";
		await _cache.InvalidatePatternAsync(pattern);
	}

	private static string GetOptimizationsHash(List<string> optimizations) {
		string combined = string.Join("|", optimizations);
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(combined)))[..16];
	}
}

/// <summary>
/// Constructs nested symbol hierarchy from flat list where parent-child relationships emerge
/// from code location containment where classes contain methods where namespaces contain
/// classes where the hierarchy enables structured navigation and analysis
/// </summary>
internal class HierarchyBuilder {
	public List<CodeSymbol> BuildHierarchy(List<CodeSymbol> flatSymbols) {
		// Group symbols by file and build nested hierarchy
		IEnumerable<IGrouping<string, CodeSymbol>> symbolsByFile = flatSymbols.GroupBy(s => s.FilePath);
		List<CodeSymbol>                           rootSymbols   = [];

		foreach (IGrouping<string, CodeSymbol> fileGroup in symbolsByFile) {
			List<CodeSymbol> fileSymbols   = fileGroup.OrderBy(s => s.StartCodeLoc.Line).ToList();
			List<CodeSymbol> nestedSymbols = BuildNestedStructure(fileSymbols);
			rootSymbols.AddRange(nestedSymbols);
		}

		return rootSymbols;
	}

	private List<CodeSymbol> BuildNestedStructure(List<CodeSymbol> symbols) {
		// Simple nesting based on position ranges
		List<CodeSymbol> roots = [];

		foreach (CodeSymbol symbol in symbols) {
			CodeSymbol? parent = FindParentSymbol(symbol, symbols);
			if (parent == null) {
				roots.Add(symbol);
			}
		}

		return roots;
	}

	private CodeSymbol? FindParentSymbol(CodeSymbol symbol, List<CodeSymbol> allSymbols) {
		return allSymbols
			.Where(s => s != symbol &&
			            s.StartCodeLoc.Line <= symbol.StartCodeLoc.Line &&
			            s.EndCodeLoc.Line >= symbol.EndCodeLoc.Line)
			.OrderBy(s => s.EndCodeLoc.Line - s.StartCodeLoc.Line)
			.FirstOrDefault();
	}
}