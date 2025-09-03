using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Thaum.Core.Models;

namespace Thaum.Core.Services;

public class HierarchicalSummarizationEngine : ISummarizationEngine
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILspClientManager _lspManager;
    private readonly ICacheService _cache;
    private readonly ILogger<HierarchicalSummarizationEngine> _logger;

    public HierarchicalSummarizationEngine(
        ILlmProvider llmProvider,
        ILspClientManager lspManager,
        ICacheService cache,
        ILogger<HierarchicalSummarizationEngine> logger)
    {
        _llmProvider = llmProvider;
        _lspManager = lspManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> SummarizeSymbolAsync(CodeSymbol symbol, SummarizationContext context, string sourceCode)
    {
        var cacheKey = $"summary_{symbol.Name}_{symbol.FilePath}_{symbol.StartPosition.Line}_{context.Level}";
        
        if (await _cache.TryGetAsync<string>(cacheKey) is { } cached)
        {
            return cached;
        }

        var prompt = BuildSummarizationPrompt(symbol, context, sourceCode);
        var summary = await _llmProvider.CompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024));
        
        await _cache.SetAsync(cacheKey, summary, TimeSpan.FromHours(24));
        return summary;
    }

    public async Task<string> ExtractCommonKeyAsync(List<string> summaries, int level)
    {
        var cacheKey = $"key_L{level}_{GetSummariesHash(summaries)}";
        
        if (await _cache.TryGetAsync<string>(cacheKey) is { } cached)
        {
            return cached;
        }

        var prompt = BuildKeyExtractionPrompt(summaries, level);
        var key = await _llmProvider.CompleteAsync(prompt, new LlmOptions(Temperature: 0.2, MaxTokens: 512));
        
        await _cache.SetAsync(cacheKey, key, TimeSpan.FromDays(1));
        return key;
    }

    public async Task<SymbolHierarchy> ProcessCodebaseAsync(string projectPath, string language)
    {
        _logger.LogInformation("Starting codebase processing for {ProjectPath}", projectPath);

        if (!await _lspManager.StartLanguageServerAsync(language, projectPath))
        {
            throw new InvalidOperationException($"Failed to start {language} language server");
        }

        var allSymbols = await _lspManager.GetWorkspaceSymbolsAsync(language, projectPath);
        var hierarchyBuilder = new HierarchyBuilder();

        // Phase 1: Summarize functions (deepest scope)
        var functions = allSymbols.Where(s => s.Kind == SymbolKind.Function || s.Kind == SymbolKind.Method).ToList();
        var functionSummaries = new List<string>();

        foreach (var function in functions)
        {
            var sourceCode = await GetSymbolSourceCode(function);
            var context = new SummarizationContext(Level: 1, AvailableKeys: new List<string>());
            var summary = await SummarizeSymbolAsync(function, context, sourceCode);
            functionSummaries.Add(summary);
            
            // Update in collection - can't assign to foreach variable
        }

        // Phase 2: Extract K1 from function summaries
        var k1 = await ExtractCommonKeyAsync(functionSummaries, 1);
        var extractedKeys = new Dictionary<string, string> { ["K1"] = k1 };

        // Phase 3: Re-summarize functions with K1
        foreach (var function in functions)
        {
            var sourceCode = await GetSymbolSourceCode(function);
            var context = new SummarizationContext(Level: 1, AvailableKeys: new List<string> { k1 });
            var summary = await SummarizeSymbolAsync(function, context, sourceCode);
            
            // Update summary with K1 - implementation simplified for demo
        }

        // Phase 4: Summarize classes with K1
        var classes = allSymbols.Where(s => s.Kind == SymbolKind.Class).ToList();
        var classSummaries = new List<string>();

        foreach (var cls in classes)
        {
            var sourceCode = await GetSymbolSourceCode(cls);
            var context = new SummarizationContext(Level: 2, AvailableKeys: new List<string> { k1 });
            var summary = await SummarizeSymbolAsync(cls, context, sourceCode);
            classSummaries.Add(summary);
            
            // Update class summary - implementation simplified for demo
        }

        // Phase 5: Extract K2 from class summaries
        var k2 = await ExtractCommonKeyAsync(classSummaries, 2);
        extractedKeys["K2"] = k2;

        // Phase 6: Re-summarize everything with K1+K2
        await ResummarizeWithKeys(functions.Concat(classes).ToList(), new List<string> { k1, k2 });

        var rootSymbols = hierarchyBuilder.BuildHierarchy(allSymbols);
        
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

    private string BuildSummarizationPrompt(CodeSymbol symbol, SummarizationContext context, string sourceCode)
    {
        var sb = new StringBuilder();
        
        if (context.AvailableKeys.Any())
        {
            sb.AppendLine("Given the following summarization keys from previous analysis:");
            foreach (var key in context.AvailableKeys)
            {
                sb.AppendLine($"- {key}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"Summarize this {symbol.Kind.ToString().ToLower()} concisely, focusing on its purpose and key functionality:");
        sb.AppendLine($"Name: {symbol.Name}");
        sb.AppendLine("Source Code:");
        sb.AppendLine(sourceCode);
        sb.AppendLine();
        sb.AppendLine("Provide a 1-2 sentence summary that captures the essential purpose and behavior.");

        return sb.ToString();
    }

    private string BuildKeyExtractionPrompt(List<string> summaries, int level)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"Analyze the following {summaries.Count} summaries from level {level} and extract a common key or pattern:");
        sb.AppendLine();
        
        for (int i = 0; i < summaries.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {summaries[i]}");
        }
        
        sb.AppendLine();
        sb.AppendLine("Extract a concise key (1-2 sentences) that captures the common patterns, themes, or architectural principles across these summaries. This key will be used to improve future summarizations at this level.");

        return sb.ToString();
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

    private async Task ResummarizeWithKeys(List<CodeSymbol> symbols, List<string> keys)
    {
        foreach (var symbol in symbols)
        {
            var sourceCode = await GetSymbolSourceCode(symbol);
            var context = new SummarizationContext(
                Level: symbol.Kind == SymbolKind.Function || symbol.Kind == SymbolKind.Method ? 1 : 2,
                AvailableKeys: keys
            );
            
            var summary = await SummarizeSymbolAsync(symbol, context, sourceCode);
            // Update symbol with new summary - this would need proper immutable update
        }
    }

    private async Task InvalidateCacheForChange(SymbolChange change)
    {
        // Implement cache invalidation logic based on the change
        var pattern = $"summary_{change.Symbol?.Name ?? "*"}_{change.FilePath}*";
        await _cache.InvalidatePatternAsync(pattern);
    }

    private static string GetSummariesHash(List<string> summaries)
    {
        var combined = string.Join("|", summaries);
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