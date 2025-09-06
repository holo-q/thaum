namespace Thaum.CLI;

public record CachedOptimization {
	public string         SymbolName   { get; init; } = "";
	public string         FilePath     { get; init; } = "";
	public int            Line         { get; init; }
	public string         Compression  { get; init; } = "";
	public string?        PromptName   { get; init; }
	public string?        ModelName    { get; init; }
	public string?        ProviderName { get; init; }
	public DateTimeOffset CreatedAt    { get; init; }
	public DateTimeOffset LastAccessed { get; init; }
}