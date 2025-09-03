using Microsoft.Extensions.Logging;

namespace Thaum.Core.Services;

// Temporary no-op cache service to get the app running
public class NoOpCacheService : ICacheService
{
    private readonly ILogger<NoOpCacheService> _logger;

    public NoOpCacheService(ILogger<NoOpCacheService> logger)
    {
        _logger = logger;
        _logger.LogInformation("Using no-op cache service - no persistence");
    }

    public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
    public Task<T?> TryGetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class => Task.CompletedTask;
    public Task RemoveAsync(string key) => Task.CompletedTask;
    public Task InvalidatePatternAsync(string pattern) => Task.CompletedTask;
    public Task ClearAsync() => Task.CompletedTask;
    public Task<bool> ExistsAsync(string key) => Task.FromResult(false);
    public Task<long> GetSizeAsync() => Task.FromResult(0L);
    public Task CompactAsync() => Task.CompletedTask;
    public void Dispose() { }
}