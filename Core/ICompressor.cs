using Thaum.Core.Models;

namespace Thaum.Core.Services;

public interface ICompressor {
	Task<string>          OptimizeSymbolAsync(CodeSymbol       symbol,      OptimizationContext context,  string           sourceCode);
	Task<string>          ExtractCommonKeyAsync(List<string>   summaries,   int                 level,    CompressionLevel compressionLevel = CompressionLevel.Optimize);
	Task<SymbolHierarchy> ProcessCodebaseAsync(string          projectPath, string              language, CompressionLevel compressionLevel = CompressionLevel.Optimize);
	Task<SymbolHierarchy> UpdateHierarchyAsync(SymbolHierarchy existing,    List<SymbolChange>  changes);
	Task<string>          GetOptimizedPromptAsync(string       basePrompt,  List<string>        examples, string task);
}

public interface ILLM {
	Task<string>                   CompleteAsync(string           prompt,       LlmOptions? options                         = null);
	Task<string>                   CompleteWithSystemAsync(string systemPrompt, string      userPrompt, LlmOptions? options = null);
	Task<IAsyncEnumerable<string>> StreamCompleteAsync(string     prompt,       LlmOptions? options = null);
}

public record LlmOptions(
	double        Temperature   = 0.7,
	int           MaxTokens     = 4096,
	string?       Model         = null,
	List<string>? StopSequences = null
);