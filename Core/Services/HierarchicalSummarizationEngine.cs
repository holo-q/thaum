using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Thaum.Core.Models;
using Thaum.Core.Utils;

namespace Thaum.Core.Services;

public class HierarchicalSummarizationEngine : ISummarizationEngine
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILspClientManager _lspManager;
    private readonly ICacheService _cache;
    private readonly IPromptLoader _promptLoader;
    private readonly ILogger<HierarchicalSummarizationEngine> _logger;

    public HierarchicalSummarizationEngine(
        ILlmProvider llmProvider,
        ILspClientManager lspManager,
        ICacheService cache,
        IPromptLoader promptLoader,
        ILogger<HierarchicalSummarizationEngine> logger)
    {
        _llmProvider = llmProvider;
        _lspManager = lspManager;
        _cache = cache;
        _promptLoader = promptLoader;
        _logger = logger;
    }

    public async Task<string> OptimizeSymbolAsync(CodeSymbol symbol, OptimizationContext context, string sourceCode)
    {
        var cacheKey = $"optimization_{symbol.Name}_{symbol.FilePath}_{symbol.StartPosition.Line}_{context.Level}";
        
        if (await _cache.TryGetAsync<string>(cacheKey) is { } cached)
        {
            return cached;
        }

        var prompt = await BuildOptimizationPromptAsync(symbol, context, sourceCode);
        var summary = await _llmProvider.CompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024));
        
        await _cache.SetAsync(cacheKey, summary, TimeSpan.FromHours(24));
        return summary;
    }

    public async Task<string> ExtractCommonKeyAsync(List<string> summaries, int level, CompressionLevel compressionLevel = CompressionLevel.Optimize)
    {
        var cacheKey = $"key_L{level}_{GetOptimizationsHash(summaries)}";
        
        if (await _cache.TryGetAsync<string>(cacheKey) is { } cached)
        {
            return cached;
        }

        var prompt = await BuildKeyExtractionPromptAsync(summaries, level, compressionLevel);
        var key = await _llmProvider.CompleteAsync(prompt, new LlmOptions(Temperature: 0.2, MaxTokens: 512));
        
        await _cache.SetAsync(cacheKey, key, TimeSpan.FromDays(1));
        return key;
    }

    public async Task<SymbolHierarchy> ProcessCodebaseAsync(string projectPath, string language, CompressionLevel compressionLevel = CompressionLevel.Optimize)
    {
        TraceFormatter.PrintHeader("HIERARCHICAL CODEBASE ANALYSIS");
        _logger.LogInformation("Starting codebase processing for {ProjectPath}", projectPath);

        TraceFormatter.PrintTrace("LSP Server", $"{language.ToUpper()}", "INIT");
        
        if (!await _lspManager.StartLanguageServerAsync(language, projectPath))
        {
            throw new InvalidOperationException($"Failed to start {language} language server");
        }

        TraceFormatter.PrintTrace("Workspace", "Symbol Discovery", "SCAN");
        var allSymbols = await _lspManager.GetWorkspaceSymbolsAsync(language, projectPath);
        var hierarchyBuilder = new HierarchyBuilder();

        // Phase 1: Optimize functions (deepest scope)
        TraceFormatter.PrintHeader("PHASE 1: FUNCTION ANALYSIS");
        var functions = allSymbols.Where(s => s.Kind == SymbolKind.Function || s.Kind == SymbolKind.Method).ToList();
        
        TraceFormatter.PrintTrace("Parallel Processing", $"{functions.Count} functions", "START");
        
        var functionOptimizationTasks = functions.Select(async function =>
        {
            TraceFormatter.PrintTrace(function.Name, "LLM Analysis", "STREAM");
            var sourceCode = await GetSymbolSourceCode(function);
            var context = new OptimizationContext(Level: 1, AvailableKeys: new List<string>(), CompressionLevel: compressionLevel);
            return await OptimizeSymbolWithStreamAsync(function, context, sourceCode);
        });

        var functionOptimizations = (await Task.WhenAll(functionOptimizationTasks)).ToList();

        // Phase 2: Extract K1 from function summaries
        TraceFormatter.PrintHeader("PHASE 2: K1 EXTRACTION");
        TraceFormatter.PrintTrace("Function Optimizations", "Pattern Analysis", "KEY");
        var k1 = await ExtractCommonKeyWithStreamAsync(functionOptimizations, 1, compressionLevel);
        var extractedKeys = new Dictionary<string, string> { ["K1"] = k1 };
        TraceFormatter.PrintTrace("K1 Extracted", k1.Length > 50 ? k1[..47] + "..." : k1, "DONE");

        // Phase 3: Re-summarize functions with K1
        TraceFormatter.PrintHeader("PHASE 3: FUNCTION RE-ANALYSIS WITH K1");
        TraceFormatter.PrintTrace("Parallel Re-analysis", $"{functions.Count} functions", "START");
        
        var functionReanalysisTasks = functions.Select(async function =>
        {
            TraceFormatter.PrintTrace(function.Name, "K1-Enhanced Analysis", "STREAM");
            var sourceCode = await GetSymbolSourceCode(function);
            var context = new OptimizationContext(Level: 1, AvailableKeys: new List<string> { k1 }, CompressionLevel: compressionLevel);
            return await OptimizeSymbolWithStreamAsync(function, context, sourceCode);
        });

        await Task.WhenAll(functionReanalysisTasks);

        // Phase 4: Optimize classes with K1
        TraceFormatter.PrintHeader("PHASE 4: CLASS ANALYSIS WITH K1");
        var classes = allSymbols.Where(s => s.Kind == SymbolKind.Class).ToList();
        
        TraceFormatter.PrintTrace("Parallel Processing", $"{classes.Count} classes", "START");
        
        var classOptimizationTasks = classes.Select(async cls =>
        {
            TraceFormatter.PrintTrace(cls.Name, "K1-Enhanced Analysis", "STREAM");
            var sourceCode = await GetSymbolSourceCode(cls);
            var context = new OptimizationContext(Level: 2, AvailableKeys: new List<string> { k1 }, CompressionLevel: compressionLevel);
            return await OptimizeSymbolWithStreamAsync(cls, context, sourceCode);
        });

        var classOptimizations = (await Task.WhenAll(classOptimizationTasks)).ToList();

        // Phase 5: Extract K2 from class summaries
        TraceFormatter.PrintHeader("PHASE 5: K2 EXTRACTION");
        TraceFormatter.PrintTrace("Class Optimizations", "Pattern Analysis", "KEY");
        var k2 = await ExtractCommonKeyWithStreamAsync(classOptimizations, 2, compressionLevel);
        extractedKeys["K2"] = k2;
        TraceFormatter.PrintTrace("K2 Extracted", k2.Length > 50 ? k2[..47] + "..." : k2, "DONE");

        // Phase 6: Re-summarize everything with K1+K2
        TraceFormatter.PrintHeader("PHASE 6: FINAL RE-ANALYSIS WITH K1+K2");
        await ResummarizeWithKeysAsync(functions.Concat(classes).ToList(), new List<string> { k1, k2 }, compressionLevel);

        TraceFormatter.PrintHeader("HIERARCHY CONSTRUCTION");
        TraceFormatter.PrintTrace("Flat Symbols", "Nested Structure", "BUILD");
        var rootSymbols = hierarchyBuilder.BuildHierarchy(allSymbols);
        
        TraceFormatter.PrintTrace("Analysis Complete", $"{allSymbols.Count} symbols processed", "DONE");
        return new SymbolHierarchy(projectPath, rootSymbols, extractedKeys, DateTime.UtcNow);
    }

    public async Task<SymbolHierarchy> UpdateHierarchyAsync(SymbolHierarchy existing, List<SymbolChange> changes)
    {
        // Implement incremental update logic
        foreach (var change in changes)
        {
            await InvalidateCacheForChange(change);
        }

        // Re-process only affected symbols and their dependencies
        return existing; // Placeholder - implement incremental logic
    }

    public async Task<string> GetOptimizedPromptAsync(string basePrompt, List<string> examples, string task)
    {
        var optimizationPrompt = $"""
            You are an expert prompt optimizer. Given the base prompt and examples below, 
            optimize the prompt for the specific task: {task}

            Base Prompt:
            {basePrompt}

            Examples:
            {string.Join("\n\n", examples)}

            Provide an optimized version that will produce better results for this task.
            Focus on clarity, specificity, and effectiveness.
            """;

        return await _llmProvider.CompleteAsync(optimizationPrompt, new LlmOptions(Temperature: 0.3));
    }

    private async Task<string> BuildOptimizationPromptAsync(CodeSymbol symbol, OptimizationContext context, string sourceCode)
    {
        var compressionPrefix = context.CompressionLevel.GetPromptPrefix();
        var symbolType = symbol.Kind switch
        {
            SymbolKind.Function or SymbolKind.Method => "function",
            SymbolKind.Class => "class",
            _ => "function"
        };
        
        var promptName = $"{compressionPrefix}_{symbolType}";

        var parameters = new Dictionary<string, object>
        {
            ["sourceCode"] = sourceCode,
            ["symbolName"] = symbol.Name,
            ["availableKeys"] = context.AvailableKeys.Any() 
                ? string.Join("\n", context.AvailableKeys.Select(k => $"- {k}")) 
                : "None"
        };

        return await _promptLoader.FormatPromptAsync(promptName, parameters);
    }

    private async Task<string> BuildKeyExtractionPromptAsync(List<string> summaries, int level, CompressionLevel compressionLevel = CompressionLevel.Optimize)
    {
        var compressionPrefix = compressionLevel.GetPromptPrefix();
        var promptName = $"{compressionPrefix}_key";
        
        var parameters = new Dictionary<string, object>
        {
            ["summaries"] = string.Join("\n", summaries.Select((s, i) => $"{i + 1}. {s}")),
            ["level"] = level.ToString()
        };

        return await _promptLoader.FormatPromptAsync(promptName, parameters);
    }

    private async Task<string> GetSymbolSourceCode(CodeSymbol symbol)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(symbol.FilePath);
            var startLine = Math.Max(0, symbol.StartPosition.Line);
            var endLine = Math.Min(lines.Length - 1, symbol.EndPosition.Line);
            
            return string.Join("\n", lines[startLine..(endLine + 1)]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read source code for symbol {SymbolName}", symbol.Name);
            return "";
        }
    }

    private async Task<string> OptimizeSymbolWithStreamAsync(CodeSymbol symbol, OptimizationContext context, string sourceCode)
    {
        var cacheKey = $"optimization_{symbol.Name}_{symbol.FilePath}_{symbol.StartPosition.Line}_{context.Level}";
        
        if (await _cache.TryGetAsync<string>(cacheKey) is { } cached)
        {
            TraceFormatter.PrintTrace(symbol.Name, "Cached Optimization", "HIT");
            return cached;
        }

        var prompt = await BuildOptimizationPromptAsync(symbol, context, sourceCode);
        
        // Stream the response in real-time
        var streamResponse = await _llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024));
        var summary = new StringBuilder();
        
        await foreach (var token in streamResponse)
        {
            summary.Append(token);
            // Real-time display would go here - for now just continue collecting
        }
        
        var result = summary.ToString().Trim();
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromHours(24));
        TraceFormatter.PrintTrace(symbol.Name, $"Optimization ({result.Length} chars)", "DONE");
        
        return result;
    }
    
    private async Task<string> ExtractCommonKeyWithStreamAsync(List<string> summaries, int level, CompressionLevel compressionLevel = CompressionLevel.Optimize)
    {
        var cacheKey = $"key_L{level}_{GetOptimizationsHash(summaries)}";
        
        if (await _cache.TryGetAsync<string>(cacheKey) is { } cached)
        {
            TraceFormatter.PrintTrace($"K{level} Extraction", "Cached Key", "HIT");
            return cached;
        }

        var prompt = await BuildKeyExtractionPromptAsync(summaries, level, compressionLevel);
        
        // Stream the key extraction response
        var streamResponse = await _llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.2, MaxTokens: 512));
        var key = new StringBuilder();
        
        await foreach (var token in streamResponse)
        {
            key.Append(token);
            // Real-time display of key extraction
        }
        
        var result = key.ToString().Trim();
        await _cache.SetAsync(cacheKey, result, TimeSpan.FromDays(1));
        
        return result;
    }

    private async Task ResummarizeWithKeysAsync(List<CodeSymbol> symbols, List<string> keys, CompressionLevel compressionLevel)
    {
        TraceFormatter.PrintTrace("Parallel Final Analysis", $"{symbols.Count} symbols", "START");
        
        var reanalysisTasks = symbols.Select(async symbol =>
        {
            TraceFormatter.PrintTrace(symbol.Name, "Final K1+K2 Analysis", "STREAM");
            var sourceCode = await GetSymbolSourceCode(symbol);
            var context = new OptimizationContext(
                Level: symbol.Kind == SymbolKind.Function || symbol.Kind == SymbolKind.Method ? 1 : 2,
                AvailableKeys: keys,
                CompressionLevel: compressionLevel
            );
            return await OptimizeSymbolWithStreamAsync(symbol, context, sourceCode);
        });

        await Task.WhenAll(reanalysisTasks);
    }

    private async Task InvalidateCacheForChange(SymbolChange change)
    {
        // Implement cache invalidation logic based on the change
        var pattern = $"optimization_{change.Symbol?.Name ?? "*"}_{change.FilePath}*";
        await _cache.InvalidatePatternAsync(pattern);
    }

    private static string GetOptimizationsHash(List<string> optimizations)
    {
        var combined = string.Join("|", optimizations);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(combined)))[..16];
    }
}

internal class HierarchyBuilder
{
    public List<CodeSymbol> BuildHierarchy(List<CodeSymbol> flatSymbols)
    {
        // Group symbols by file and build nested hierarchy
        var symbolsByFile = flatSymbols.GroupBy(s => s.FilePath);
        var rootSymbols = new List<CodeSymbol>();

        foreach (var fileGroup in symbolsByFile)
        {
            var fileSymbols = fileGroup.OrderBy(s => s.StartPosition.Line).ToList();
            var nestedSymbols = BuildNestedStructure(fileSymbols);
            rootSymbols.AddRange(nestedSymbols);
        }

        return rootSymbols;
    }

    private List<CodeSymbol> BuildNestedStructure(List<CodeSymbol> symbols)
    {
        // Simple nesting based on position ranges
        var roots = new List<CodeSymbol>();
        
        foreach (var symbol in symbols)
        {
            var parent = FindParentSymbol(symbol, symbols);
            if (parent == null)
            {
                roots.Add(symbol);
            }
        }

        return roots;
    }

    private CodeSymbol? FindParentSymbol(CodeSymbol symbol, List<CodeSymbol> allSymbols)
    {
        return allSymbols
            .Where(s => s != symbol && 
                       s.StartPosition.Line <= symbol.StartPosition.Line && 
                       s.EndPosition.Line >= symbol.EndPosition.Line)
            .OrderBy(s => s.EndPosition.Line - s.StartPosition.Line)
            .FirstOrDefault();
    }
}