using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Thaum.Core.Services;

public class SqliteCacheService : ICacheService
{
    private readonly ILogger<SqliteCacheService> _logger;
    private readonly SqliteConnection _connection;
    private readonly JsonSerializerOptions _jsonOptions;

    public SqliteCacheService(IConfiguration configuration, ILogger<SqliteCacheService> logger)
    {
        _logger = logger;
        
        var cacheDir = configuration["Cache:Directory"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thaum");
        Directory.CreateDirectory(cacheDir);
        
        var dbPath = Path.Combine(cacheDir, "cache.db");
        var connectionString = $"Data Source={dbPath}";
        
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        var createTableSql = """
            CREATE TABLE IF NOT EXISTS cache_entries (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                type_name TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                expires_at INTEGER,
                last_accessed INTEGER NOT NULL
            );
            
            CREATE INDEX IF NOT EXISTS idx_expires_at ON cache_entries(expires_at);
            CREATE INDEX IF NOT EXISTS idx_last_accessed ON cache_entries(last_accessed);
            CREATE INDEX IF NOT EXISTS idx_key_pattern ON cache_entries(key);
            """;

        using var command = new SqliteCommand(createTableSql, _connection);
        command.ExecuteNonQuery();
        
        _logger.LogDebug("Cache database initialized");
    }

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            var sql = """
                SELECT value, type_name 
                FROM cache_entries 
                WHERE key = @key 
                AND (expires_at IS NULL OR expires_at > @now)
                """;

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@now", now);

            using var reader = await command.ExecuteReaderAsync();
            
            if (!await reader.ReadAsync())
            {
                return null;
            }

            var value = reader.GetString("value");
            var typeName = reader.GetString("type_name");
            
            // Update last accessed time
            await UpdateLastAccessedAsync(key, now);
            
            return JsonSerializer.Deserialize<T>(value, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached value for key {Key}", key);
            return null;
        }
    }

    public async Task<T?> TryGetAsync<T>(string key) where T : class
    {
        return await GetAsync<T>(key);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAt = expiration.HasValue ? now + (long)expiration.Value.TotalSeconds : (long?)null;
            
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            var typeName = typeof(T).FullName ?? typeof(T).Name;
            
            var sql = """
                INSERT OR REPLACE INTO cache_entries 
                (key, value, type_name, created_at, expires_at, last_accessed)
                VALUES (@key, @value, @typeName, @now, @expiresAt, @now)
                """;

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", json);
            command.Parameters.AddWithValue("@typeName", typeName);
            command.Parameters.AddWithValue("@now", now);
            command.Parameters.AddWithValue("@expiresAt", expiresAt);

            await command.ExecuteNonQueryAsync();
            
            _logger.LogTrace("Cached value for key {Key} with expiration {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching value for key {Key}", key);
            throw;
        }
    }

    public async Task RemoveAsync(string key)
    {
        try
        {
            var sql = "DELETE FROM cache_entries WHERE key = @key";
            
            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@key", key);
            
            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            if (rowsAffected > 0)
            {
                _logger.LogTrace("Removed cached entry for key {Key}", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing cached value for key {Key}", key);
            throw;
        }
    }

    public async Task InvalidatePatternAsync(string pattern)
    {
        try
        {
            // Convert simple wildcard pattern to SQL LIKE pattern
            var likePattern = pattern.Replace("*", "%").Replace("?", "_");
            
            var sql = "DELETE FROM cache_entries WHERE key LIKE @pattern";
            
            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@pattern", likePattern);
            
            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            _logger.LogDebug("Invalidated {Count} cache entries matching pattern {Pattern}", rowsAffected, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache entries with pattern {Pattern}", pattern);
            throw;
        }
    }

    public async Task ClearAsync()
    {
        try
        {
            var sql = "DELETE FROM cache_entries";
            
            using var command = new SqliteCommand(sql, _connection);
            var rowsAffected = await command.ExecuteNonQueryAsync();
            
            _logger.LogInformation("Cleared {Count} cache entries", rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            var sql = """
                SELECT 1 FROM cache_entries 
                WHERE key = @key 
                AND (expires_at IS NULL OR expires_at > @now)
                LIMIT 1
                """;

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@now", now);

            using var reader = await command.ExecuteReaderAsync();
            return await reader.ReadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if key exists {Key}", key);
            return false;
        }
    }

    public async Task<long> GetSizeAsync()
    {
        try
        {
            var sql = "SELECT COUNT(*) FROM cache_entries";
            
            using var command = new SqliteCommand(sql, _connection);
            var result = await command.ExecuteScalarAsync();
            
            return Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache size");
            return 0;
        }
    }

    public async Task CompactAsync()
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            
            // Remove expired entries
            var cleanupSql = "DELETE FROM cache_entries WHERE expires_at IS NOT NULL AND expires_at <= @now";
            
            using var cleanupCommand = new SqliteCommand(cleanupSql, _connection);
            cleanupCommand.Parameters.AddWithValue("@now", now);
            var expiredCount = await cleanupCommand.ExecuteNonQueryAsync();
            
            // Vacuum database to reclaim space
            var vacuumSql = "VACUUM";
            using var vacuumCommand = new SqliteCommand(vacuumSql, _connection);
            await vacuumCommand.ExecuteNonQueryAsync();
            
            _logger.LogInformation("Cache compaction completed: removed {ExpiredCount} expired entries", expiredCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error compacting cache");
            throw;
        }
    }

    private async Task UpdateLastAccessedAsync(string key, long timestamp)
    {
        try
        {
            var sql = "UPDATE cache_entries SET last_accessed = @timestamp WHERE key = @key";
            
            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@timestamp", timestamp);
            
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error updating last accessed time for key {Key}", key);
            // Non-critical error, don't throw
        }
    }

    public void Dispose()
    {
        try
        {
            _connection?.Close();
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing cache service");
        }
    }
}