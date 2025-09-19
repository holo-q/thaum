namespace Thaum.Core.Cache;

public interface ICache : IDisposable {
	Task<T?>                   GetAsync<T>(string            key) where T : class;
	Task<T?>                   TryGetAsync<T>(string         key) where T : class;
	Task                       SetAsync<T>(string            key, T value, TimeSpan? expiration = null, string? promptName = null, string? promptContent = null, string? modelName = null, string? providerName = null) where T : class;
	Task                       RemoveAsync(string            key);
	Task                       InvalidatePatternAsync(string pattern);
	Task                       ClearAsync();
	Task<bool>                 ExistsAsync(string key);
	Task<long>                 GetSizeAsync();
	Task                       CompactAsync();
	Task<List<CacheEntryInfo>> GetAllEntriesAsync();
}