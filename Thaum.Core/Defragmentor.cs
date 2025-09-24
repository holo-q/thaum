using System.Text;
using Microsoft.Extensions.Logging;
using Ratatui;
using Spectre.Console;
using Thaum.Core.Cache;
using Thaum.Core.Crawling;
using Style = Spectre.Console.Style;

namespace Thaum.Core;

public record CompressorOptions(string ProjectPath, string Language, string? DefaultPromptName = null);

/// <summary>
/// Context for hierarchical compression where Level indicates depth (1=function 2=class) where
/// AvailableKeys contains discovered semantic patterns from previous phases where each level
/// builds on previous discoveries creating progressive refinement of understanding
/// </summary>
public record OptimizationContext(
	int           Level,
	List<string>  AvailableKeys,
	string?       PromptName      = null,
	string?       ParentContext   = null,
	List<string>? SiblingContexts = null
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
[LoggingIntrinsics]
public partial class Defragmentor {
	private readonly LLM                   _llm;
	private readonly Crawler               _crawler;
	private readonly ICache                _cache;
	private readonly PromptLoader          _promptLoader;
	private readonly ILogger<Defragmentor> _logger;

	public Defragmentor(LLM llm, Crawler crawler, ICache cache, PromptLoader promptLoader) {
		_llm          = llm;
		_crawler      = crawler;
		_cache        = cache;
		_promptLoader = promptLoader;
		_logger       = RatLog.Get<Defragmentor>();
	}

	/// <summary>
	/// Compresses single symbol where caching checks prevent redundant work where prompt
	/// construction uses discovered keys where LLM temperature 0.3 balances consistency
	/// with creativity where result caching enables incremental processing
	/// </summary>
	public async Task<string> RewriteAsync(CodeSymbol sym, OptimizationContext ctx, string code) { // TODO take enum of the rewrite operationa to perform
		string cacheKey = $"optimization_{sym.Name}_{sym.FilePath}_{sym.StartCodeLoc.Line}_{ctx.Level}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			return cached;
		}

		// Get the prompt name - use context override or default
		string promptName = ctx.PromptName ?? GLB.GetDefaultPrompt(sym);
		string prompt     = await BuildOptimizationPromptAsync(sym, ctx, code, promptName);

		// Get the raw prompt template content
		string promptContent = await _promptLoader.LoadPrompt(promptName);

		// Get model from configuration - fail fast if not available
		// TODO lots of stuff that should be moved to Glb
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		string summary = await _llm.CompleteAsync(prompt, GLB.CompressionOptions(model));

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
	public async Task<string> ExtractCommonKeyAsync(List<string> summaries, int level, string? keyPromptName = null) {
		string cacheKey = $"key_L{level}_{GetOptimizationsHash(summaries)}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			return cached;
		}

		string actualKeyPromptName = keyPromptName ?? GLB.DefaultKeyPrompt;
		string prompt              = await BuildKeyExtractionPromptAsync(summaries, level, actualKeyPromptName);

		// Get prompt content for metadata
		string promptContent = await _promptLoader.LoadPrompt(actualKeyPromptName);

		// Get model from configuration - fail fast if not available
		// TODO lots of stuff that should be moved to Glb
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		string key = await _llm.CompleteAsync(prompt, GLB.KeyOptions(model));

		// Determine provider from model name (simple heuristic)
		string provider = model.Contains("gpt") ? "openai" :
			model.Contains("claude")            ? "anthropic" :
			model.Contains("llama")             ? "ollama" : "unknown";

		await _cache.SetAsync(cacheKey, key, TimeSpan.FromDays(1), actualKeyPromptName, promptContent, model, provider);
		return key;
	}

	/// <summary>
	/// Six-phase hierarchical compression pipeline where Phase1=function analysis where
	/// Phase2=K1 extraction where Phase3=function reanalysis with K1 where Phase4=class
	/// analysis with K1 where Phase5=K2 extraction where Phase6=final reanalysis with K1+K2
	/// where parallel processing maximizes throughput where streaming enables progress tracking
	/// </summary>
	public async Task<SymbolHierarchy> ProcessCodebaseAsync(string projectPath, string language, string? defaultPromptName = null) {
		info("Start: codebase processing for {ProjectPath}", projectPath);

		// Discovery spinner (compact, no boxes)
		CodeMap codeMap = await AnsiConsole.Status()
			.Spinner(Spinner.Known.Dots)
			.SpinnerStyle(Style.Parse("green"))
			.StartAsync("Scanning workspace for symbols", async _ => await _crawler.CrawlDir(projectPath));
		List<CodeSymbol> allSymbols       = codeMap.ToList();
		HierarchyBuilder hierarchyBuilder = new HierarchyBuilder();

		// Phase 1: Optimize functions (deepest scope)
		using IDisposable p1        = RatLog.Scope("PHASE 1: Function Analysis");
		List<CodeSymbol>  functions = allSymbols.Where(s => s.Kind is SymbolKind.Function or SymbolKind.Method).ToList();

		List<string> functionOptimizations = new List<string>(functions.Count);
		await AnsiConsole.Progress()
			.Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
			.StartAsync(async ctx => {
				ProgressTask task = ctx.AddTask($"Functions ({functions.Count})", maxValue: functions.Count);
				string[] results = await Task.WhenAll(functions.Select(async function => {
					string              sourceCode = await GetSymbolSourceCode(function);
					OptimizationContext context    = new OptimizationContext(Level: 1, AvailableKeys: [], PromptName: defaultPromptName);
					string              summary    = await OptimizeSymbolWithStreamAsync(function, context, sourceCode);
					task.Increment(1);
					return summary;
				}));
				functionOptimizations.AddRange(results);
			});

		// Phase 2: Extract K1 from function summaries
		using IDisposable p2 = RatLog.Scope("PHASE 2: K1 Extraction");
		string k1 = await AnsiConsole.Status()
			.Spinner(Spinner.Known.Dots)
			.SpinnerStyle(Style.Parse("yellow"))
			.StartAsync("Extracting K1 from function summaries", async _ => await ExtractCommonKeyWithStreamAsync(functionOptimizations, 1));
		Dictionary<string, string> extractedKeys = new Dictionary<string, string> { ["K1"] = k1 };
		info("K1: {Sample}", k1.Length > 50 ? $"{k1[..47]}..." : k1);

		// Phase 3: Re-summarize functions with K1
		using IDisposable p3 = RatLog.Scope("PHASE 3: Function Re-analysis with K1");
		await AnsiConsole.Progress()
			.Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
			.StartAsync(async ctx => {
				ProgressTask task = ctx.AddTask($"Functions ({functions.Count})", maxValue: functions.Count);
				await Task.WhenAll(functions.Select(async function => {
					string              sourceCode = await GetSymbolSourceCode(function);
					OptimizationContext context    = new OptimizationContext(Level: 1, AvailableKeys: [k1], PromptName: defaultPromptName);
					await OptimizeSymbolWithStreamAsync(function, context, sourceCode);
					task.Increment(1);
				}));
			});

		// Phase 4: Optimize classes with K1
		using IDisposable p4      = RatLog.Scope("PHASE 4: Class Analysis with K1");
		List<CodeSymbol>  classes = allSymbols.Where(s => s.Kind == SymbolKind.Class).ToList();

		List<string> classOptimizations = new List<string>(classes.Count);
		await AnsiConsole.Progress()
			.Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
			.StartAsync(async ctx => {
				ProgressTask task = ctx.AddTask($"Classes ({classes.Count})", maxValue: classes.Count);
				string[] results = await Task.WhenAll(classes.Select(async cls => {
					string              sourceCode = await GetSymbolSourceCode(cls);
					OptimizationContext context    = new OptimizationContext(Level: 2, AvailableKeys: [k1], PromptName: defaultPromptName);
					string              summary    = await OptimizeSymbolWithStreamAsync(cls, context, sourceCode);
					task.Increment(1);
					return summary;
				}));
				classOptimizations.AddRange(results);
			});

		// Phase 5: Extract K2 from class summaries
		using IDisposable p5 = RatLog.Scope("PHASE 5: K2 Extraction");
		string k2 = await AnsiConsole.Status()
			.Spinner(Spinner.Known.Dots)
			.SpinnerStyle(Style.Parse("yellow"))
			.StartAsync("Extracting K2 from class summaries", async _ => await ExtractCommonKeyWithStreamAsync(classOptimizations, 2));
		extractedKeys["K2"] = k2;
		info("K2: {Sample}", k2.Length > 50 ? $"{k2[..47]}..." : k2);

		// Phase 6: Re-summarize everything with K1+K2
		using IDisposable p6  = RatLog.Scope("PHASE 6: Final Re-analysis with K1+K2");
		List<CodeSymbol>  all = functions.Concat(classes).ToList();
		await AnsiConsole.Progress()
			.Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new RemainingTimeColumn())
			.StartAsync(async ctx => {
				ProgressTask task = ctx.AddTask($"Symbols ({all.Count})", maxValue: all.Count);
				await Task.WhenAll(all.Select(async symbol => {
					string sourceCode = await GetSymbolSourceCode(symbol);
					OptimizationContext context = new OptimizationContext(
						Level: symbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
						AvailableKeys: [k1, k2],
						PromptName: defaultPromptName);
					await OptimizeSymbolWithStreamAsync(symbol, context, sourceCode);
					task.Increment(1);
				}));
			});

		using IDisposable p7          = RatLog.Scope("Hierarchy Construction");
		List<CodeSymbol>  rootSymbols = hierarchyBuilder.BuildHierarchy(allSymbols);

		info("Done: {Count} symbols processed", allSymbols.Count);
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

	private async Task<string> BuildOptimizationPromptAsync(CodeSymbol symbol, OptimizationContext context, string sourceCode, string promptName) {
		// Prompt name is now directly passed as parameter
		string symbolType = symbol.Kind switch {
			SymbolKind.Function or SymbolKind.Method => "function",
			SymbolKind.Class                         => "class",
			_                                        => "function"
		};

		Dictionary<string, object> parameters = new Dictionary<string, object> {
			["sourceCode"] = sourceCode,
			["symbolName"] = symbol.Name,
			["availableKeys"] = context.AvailableKeys.Any()
				? string.Join("\n", context.AvailableKeys.Select(k => $"- {k}"))
				: "None"
		};

		return await _promptLoader.FormatPrompt(promptName, parameters);
	}

	private async Task<string> BuildKeyExtractionPromptAsync(List<string> summaries, int level, string keyPromptName) {
		string promptName = keyPromptName;

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
			err(ex, "Failed to read source code for symbol {SymbolName}", symbol.Name);
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
			trace("Cache hit: {Symbol}", symbol.Name);
			return cached;
		}

		string promptName = context.PromptName ?? GLB.GetDefaultPrompt(symbol);
		string prompt     = await BuildOptimizationPromptAsync(symbol, context, sourceCode, promptName);

		// Get model from configuration - fail fast if not available
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		// Stream the response in real-time
		IAsyncEnumerable<string> streamResponse = await _llm.StreamCompleteAsync(prompt, GLB.CompressionOptions(model));
		StringBuilder            summary        = new StringBuilder();

		await foreach (string token in streamResponse) {
			summary.Append(token);
			// Real-time display would go here - for now just continue collecting
		}

		string result = summary.ToString().Trim();
		await _cache.SetAsync(cacheKey, result, TimeSpan.FromHours(24));
		trace("Optimized {Symbol} ({Len} chars)", symbol.Name, result.Length);

		return result;
	}

	private async Task<string> ExtractCommonKeyWithStreamAsync(List<string> summaries, int level, string? keyPromptName = null) {
		string cacheKey = $"key_L{level}_{GetOptimizationsHash(summaries)}";

		if (await _cache.TryGetAsync<string>(cacheKey) is { } cached) {
			trace("Cache hit: K{Level}", level);
			return cached;
		}

		string actualKeyPromptName = keyPromptName ?? GLB.DefaultKeyPrompt;
		string prompt              = await BuildKeyExtractionPromptAsync(summaries, level, actualKeyPromptName);

		// Get model from configuration - fail fast if not available
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		// Stream the key extraction response
		IAsyncEnumerable<string> streamResponse = await _llm.StreamCompleteAsync(prompt, GLB.KeyOptions(model));
		StringBuilder            key            = new StringBuilder();

		await foreach (string token in streamResponse) {
			key.Append(token);
			// Real-time display of key extraction
		}

		string result = key.ToString().Trim();
		await _cache.SetAsync(cacheKey, result, TimeSpan.FromDays(1));

		return result;
	}

	private async Task ResummarizeWithKeysAsync(List<CodeSymbol> symbols, List<string> keys, string? defaultPromptName = null) {
		trace("Final analysis for {Count} symbols", symbols.Count);

		IEnumerable<Task<string>> reanalysisTasks = symbols.Select(async symbol => {
			trace("Final analysis: {Symbol}", symbol.Name);
			string sourceCode = await GetSymbolSourceCode(symbol);
			OptimizationContext context = new OptimizationContext(
				Level: symbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
				AvailableKeys: keys,
				PromptName: defaultPromptName
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