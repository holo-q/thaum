namespace Thaum.Core.Models;

public record CachedKey {
	public int            Level        { get; init; }
	public string         Pattern      { get; init; } = "";
	public string?        PromptName   { get; init; }
	public string?        ModelName    { get; init; }
	public string?        ProviderName { get; init; }
	public DateTimeOffset CreatedAt    { get; init; }
	public DateTimeOffset LastAccessed { get; init; }
}