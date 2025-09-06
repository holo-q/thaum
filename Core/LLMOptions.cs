namespace Thaum.Core.Services;

public record LLMOptions(
	double        Temperature   = 0.7,
	int           MaxTokens     = 4096,
	string?       Model         = null,
	List<string>? StopSequences = null
);