using Microsoft.Extensions.Logging;
using Thaum.Core.Models;

namespace Thaum.Core.Services;

// Temporary no-op cache service to get the app running
public class MockCache : ICache {
	private readonly ILogger<MockCache> _logger;

	public MockCache(ILogger<MockCache> logger) {
		_logger = logger;
		_logger.LogInformation("Using no-op cache service - no persistence");
	}

	public Task<T?>                   GetAsync<T>(string            key) where T : class                                                                                                                                                       => Task.FromResult<T?>(null);
	public Task<T?>                   TryGetAsync<T>(string         key) where T : class                                                                                                                                                       => Task.FromResult<T?>(null);
	public Task                       SetAsync<T>(string            key, T value, TimeSpan? expiration = null, string? promptName = null, string? promptContent = null, string? modelName = null, string? providerName = null) where T : class => Task.CompletedTask;
	public Task                       RemoveAsync(string            key)     => Task.CompletedTask;
	public Task                       InvalidatePatternAsync(string pattern) => Task.CompletedTask;
	public Task                       ClearAsync()                           => Task.CompletedTask;
	public Task<bool>                 ExistsAsync(string key)                => Task.FromResult(false);
	public Task<long>                 GetSizeAsync()                         => Task.FromResult(0L);
	public Task                       CompactAsync()                         => Task.CompletedTask;
	public Task<List<CacheEntryInfo>> GetAllEntriesAsync()                   => Task.FromResult(new List<CacheEntryInfo>());
	public void                       Dispose()                              { }
}