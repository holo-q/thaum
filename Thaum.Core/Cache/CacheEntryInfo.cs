namespace Thaum.Core.Cache;

public class CacheEntryInfo {
	public required string          Key               { get; init; }
	public required string          TypeName          { get; init; }
	public required string          Value             { get; init; }
	public          DateTimeOffset  CreatedAt         { get; init; }
	public          DateTimeOffset? ExpiresAt         { get; init; }
	public          DateTimeOffset  LastAccessed      { get; init; }
	public          string?         PromptName        { get; init; }
	public          string?         PromptDisplayName { get; init; }
	public          string?         ModelName         { get; init; }
	public          string?         ProviderName      { get; init; }
}