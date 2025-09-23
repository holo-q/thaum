using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Thaum.Core.Utils;

namespace Thaum.Core.Cache;

/// <summary>
/// SQLite-backed persistent cache for compression results and prompts where caching prevents
/// redundant LLM calls saving time/money where prompt hashing enables deduplication where
/// metadata tracking (model/provider/prompt) enables cache analysis where expiration/compaction
/// maintains bounded storage where pattern-based invalidation enables targeted cache clearing
/// </summary>
public class Cache : ICache {
	private readonly ILogger<Cache>        _logger;
	private readonly SqliteConnection      _con;
	private readonly JsonSerializerOptions _jsonOptions;

	/// <summary>
	/// Initializes cache with platform-appropriate directory where temp path fallback ensures
	/// universal compatibility where SQLite provides lightweight persistence without external
	/// dependencies where JSON serialization preserves type fidelity across cache boundaries
	/// </summary>
	public Cache(IConfiguration configuration) {
		_logger = Logging.Get<Cache>();

		// Simple cache directory - works on all platforms
		string cacheDir = GLB.CacheDir;

		_logger.LogDebug("Creating cache directory: {CacheDir}", cacheDir);
		Directory.CreateDirectory(cacheDir);

		string dbPath           = GLB.CacheDbPath;
		string connectionString = $"Data Source={dbPath}";

		_con = new SqliteConnection(connectionString);
		_con.Open();

		_jsonOptions = GLB.CacheJsonOptions;

		InitializeDatabase();
	}

	/// <summary>
	/// Creates/migrates SQLite schema where backward compatibility preserves existing caches
	/// where column detection enables graceful upgrades where indexes optimize common queries
	/// where prompts table deduplicates identical prompt content saving storage
	/// </summary>
	private void InitializeDatabase() {
		// First, create the basic cache_entries table if it doesn't exist
		const string CREATE_BASIC_TABLE_SQL = """
		                                      CREATE TABLE IF NOT EXISTS cache_entries (
		                                          key TEXT PRIMARY KEY,
		                                          value TEXT NOT NULL,
		                                          type_name TEXT NOT NULL,
		                                          created_at INTEGER NOT NULL,
		                                          expires_at INTEGER,
		                                          last_accessed INTEGER NOT NULL
		                                      );
		                                      """;

		using (SqliteCommand command = new SqliteCommand(CREATE_BASIC_TABLE_SQL, _con)) {
			command.ExecuteNonQuery();
		}

		// Check if new columns exist, and add them if they don't
		string checkColumnsSql = "PRAGMA table_info(cache_entries)";
		bool   hasPromptName   = false;
		bool   hasPromptHash   = false;
		bool   hasModelName    = false;
		bool   hasProviderName = false;

		using (SqliteCommand command = new SqliteCommand(checkColumnsSql, _con))
		using (SqliteDataReader reader = command.ExecuteReader()) {
			while (reader.Read()) {
				string columnName = reader.GetString(1); // column name is at index 1
				switch (columnName) {
					case "prompt_name":
						hasPromptName = true;
						break;
					case "prompt_hash":
						hasPromptHash = true;
						break;
					case "model_name":
						hasModelName = true;
						break;
					case "provider_name":
						hasProviderName = true;
						break;
				}
			}
		}

		// Add missing columns
		if (!hasPromptName) {
			using SqliteCommand command = new SqliteCommand("ALTER TABLE cache_entries ADD COLUMN prompt_name TEXT", _con);
			command.ExecuteNonQuery();
		}

		if (!hasPromptHash) {
			using SqliteCommand command = new SqliteCommand("ALTER TABLE cache_entries ADD COLUMN prompt_hash TEXT", _con);
			command.ExecuteNonQuery();
		}

		if (!hasModelName) {
			using SqliteCommand command = new SqliteCommand("ALTER TABLE cache_entries ADD COLUMN model_name TEXT", _con);
			command.ExecuteNonQuery();
		}

		if (!hasProviderName) {
			using SqliteCommand command = new SqliteCommand("ALTER TABLE cache_entries ADD COLUMN provider_name TEXT", _con);
			command.ExecuteNonQuery();
		}

		// Create prompts table
		string createPromptsTableSql = """
		                               CREATE TABLE IF NOT EXISTS prompts (
		                                   hash TEXT PRIMARY KEY,
		                                   name TEXT NOT NULL,
		                                   content TEXT NOT NULL,
		                                   created_at INTEGER NOT NULL
		                               );
		                               """;

		using (SqliteCommand command = new SqliteCommand(createPromptsTableSql, _con)) {
			command.ExecuteNonQuery();
		}

		// Create indexes
		string createIndexesSql = """
		                          CREATE INDEX IF NOT EXISTS idx_expires_at ON cache_entries(expires_at);
		                          CREATE INDEX IF NOT EXISTS idx_last_accessed ON cache_entries(last_accessed);
		                          CREATE INDEX IF NOT EXISTS idx_key_pattern ON cache_entries(key);
		                          CREATE INDEX IF NOT EXISTS idx_prompt_name ON cache_entries(prompt_name);
		                          CREATE INDEX IF NOT EXISTS idx_prompt_hash ON cache_entries(prompt_hash);
		                          CREATE INDEX IF NOT EXISTS idx_model_name ON cache_entries(model_name);
		                          CREATE INDEX IF NOT EXISTS idx_prompt_name_lookup ON prompts(name);
		                          """;

		using (SqliteCommand command = new SqliteCommand(createIndexesSql, _con)) {
			command.ExecuteNonQuery();
		}

		_logger.LogDebug("Cache database initialized with prompt and model tracking");
	}

	/// <summary>
	/// Retrieves cached value checking expiration where last-accessed tracking enables LRU
	/// eviction strategies where generic deserialization preserves type safety where null
	/// return indicates miss enabling caller to decide fallback strategy
	/// </summary>
	[RequiresUnreferencedCode("Uses reflection for JSON deserialization")]
	public async Task<T?> GetAsync<T>(string key) where T : class {
		try {
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			string sql = """
			             SELECT value, type_name 
			             FROM cache_entries 
			             WHERE key = @key 
			             AND (expires_at IS NULL OR expires_at > @now)
			             """;

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@key", key);
			command.Parameters.AddWithValue("@now", now);

			await using SqliteDataReader reader = await command.ExecuteReaderAsync();

			if (!await reader.ReadAsync()) {
				return null;
			}

			string value    = reader.GetString(0);
			string typeName = reader.GetString(1);

			// Update last accessed time
			await UpdateLastAccessedAsync(key, now);

			return JsonSerializer.Deserialize<T>(value, _jsonOptions);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting cached value for key {Key}", key);
			return null;
		}
	}

	public async Task<T?> TryGetAsync<T>(string key) where T : class {
		return await GetAsync<T>(key);
	}

	/// <summary>
	/// Stores value with optional metadata where prompt deduplication via hashing saves space
	/// where model/provider tracking enables cache analysis where expiration enables automatic
	/// cleanup where atomic upsert prevents race conditions in concurrent scenarios
	/// </summary>
	[RequiresUnreferencedCode("Uses reflection for JSON serialization")]
	public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, string? promptName = null, string? promptContent = null, string? modelName = null, string? providerName = null) where T : class {
		try {
			long  now       = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			long? expiresAt = expiration.HasValue ? now + (long)expiration.Value.TotalSeconds : (long?)null;

			string json     = JsonSerializer.Serialize(value, _jsonOptions);
			string typeName = typeof(T).FullName ?? typeof(T).Name;

			string? promptHash = null;

			// Store prompt metadata if provided
			if (!string.IsNullOrEmpty(promptName) && !string.IsNullOrEmpty(promptContent)) {
				promptHash = await StorePromptAsync(promptName, promptContent);
			}

			string sql = """
			             INSERT OR REPLACE INTO cache_entries 
			             (key, value, type_name, created_at, expires_at, last_accessed, prompt_name, prompt_hash, model_name, provider_name)
			             VALUES (@key, @value, @typeName, @now, @expiresAt, @now, @promptName, @promptHash, @modelName, @providerName)
			             """;

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@key", key);
			command.Parameters.AddWithValue("@value", json);
			command.Parameters.AddWithValue("@typeName", typeName);
			command.Parameters.AddWithValue("@now", now);
			command.Parameters.AddWithValue("@expiresAt", expiresAt.HasValue ? expiresAt.Value : DBNull.Value);
			command.Parameters.AddWithValue("@promptName", promptName ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@promptHash", promptHash ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@modelName", modelName ?? (object)DBNull.Value);
			command.Parameters.AddWithValue("@providerName", providerName ?? (object)DBNull.Value);

			await command.ExecuteNonQueryAsync();

			_logger.LogTrace("Cached value for key {Key} with prompt {PromptName}, model {ModelName}, provider {ProviderName}",
				key, promptName, modelName, providerName);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error caching value for key {Key}", key);
			throw;
		}
	}

	public async Task RemoveAsync(string key) {
		try {
			string sql = "DELETE FROM cache_entries WHERE key = @key";

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@key", key);

			int rowsAffected = await command.ExecuteNonQueryAsync();

			if (rowsAffected > 0) {
				_logger.LogTrace("Removed cached entry for key {Key}", key);
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error removing cached value for key {Key}", key);
			throw;
		}
	}

	/// <summary>
	/// Removes entries matching wildcard pattern where * matches any sequence where ? matches
	/// single character where pattern-based clearing enables targeted invalidation without
	/// full cache flush preserving unrelated cached results
	/// </summary>
	public async Task InvalidatePatternAsync(string pattern) {
		try {
			// Convert simple wildcard pattern to SQL LIKE pattern
			string likePattern = pattern.Replace("*", "%").Replace("?", "_");

			string sql = "DELETE FROM cache_entries WHERE key LIKE @pattern";

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@pattern", likePattern);

			int rowsAffected = await command.ExecuteNonQueryAsync();

			_logger.LogDebug("Invalidated {Count} cache entries matching pattern {Pattern}", rowsAffected, pattern);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error invalidating cache entries with pattern {Pattern}", pattern);
			throw;
		}
	}

	public async Task ClearAsync() {
		try {
			string sql = "DELETE FROM cache_entries";

			await using SqliteCommand command      = new SqliteCommand(sql, _con);
			int                       rowsAffected = await command.ExecuteNonQueryAsync();

			_logger.LogInformation("Cleared {Count} cache entries", rowsAffected);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error clearing cache");
			throw;
		}
	}

	public async Task<bool> ExistsAsync(string key) {
		try {
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			string sql = """
			             SELECT 1 FROM cache_entries 
			             WHERE key = @key 
			             AND (expires_at IS NULL OR expires_at > @now)
			             LIMIT 1
			             """;

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@key", key);
			command.Parameters.AddWithValue("@now", now);

			await using SqliteDataReader reader = await command.ExecuteReaderAsync();
			return await reader.ReadAsync();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error checking if key exists {Key}", key);
			return false;
		}
	}

	public async Task<long> GetSizeAsync() {
		try {
			string sql = "SELECT COUNT(*) FROM cache_entries";

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			object?                   result  = await command.ExecuteScalarAsync();

			return Convert.ToInt64(result);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting cache size");
			return 0;
		}
	}

	/// <summary>
	/// Removes expired entries and reclaims storage where VACUUM rebuilds database eliminating
	/// fragmentation where periodic compaction maintains cache performance where automatic
	/// expiration cleanup prevents unbounded growth
	/// </summary>
	public async Task CompactAsync() {
		try {
			long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			// Remove expired entries
			string cleanupSql = "DELETE FROM cache_entries WHERE expires_at IS NOT NULL AND expires_at <= @now";

			await using SqliteCommand cleanupCommand = new SqliteCommand(cleanupSql, _con);
			cleanupCommand.Parameters.AddWithValue("@now", now);
			int expiredCount = await cleanupCommand.ExecuteNonQueryAsync();

			// Vacuum database to reclaim space
			string                    vacuumSql     = "VACUUM";
			await using SqliteCommand vacuumCommand = new SqliteCommand(vacuumSql, _con);
			await vacuumCommand.ExecuteNonQueryAsync();

			_logger.LogInformation("Cache compaction completed: removed {ExpiredCount} expired entries", expiredCount);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error compacting cache");
			throw;
		}
	}

	private async Task UpdateLastAccessedAsync(string key, long timestamp) {
		try {
			string sql = "UPDATE cache_entries SET last_accessed = @timestamp WHERE key = @key";

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@key", key);
			command.Parameters.AddWithValue("@timestamp", timestamp);

			await command.ExecuteNonQueryAsync();
		} catch (Exception ex) {
			_logger.LogTrace(ex, "Error updating last accessed time for key {Key}", key);
			// Non-critical error, don't throw
		}
	}

	/// <summary>
	/// Stores prompt content deduplicating by hash where identical prompts share storage
	/// where hash enables content-based lookup where INSERT OR IGNORE prevents duplicates
	/// where prompt tracking enables analysis of which prompts produce best compressions
	/// </summary>
	private async Task<string> StorePromptAsync(string promptName, string promptContent) {
		try {
			// Generate hash for the prompt content
			string promptHash = GeneratePromptHash(promptContent);
			long   now        = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

			// Insert or ignore (if hash already exists, we don't need to store it again)
			string sql = """
			             INSERT OR IGNORE INTO prompts 
			             (hash, name, content, created_at)
			             VALUES (@hash, @name, @content, @now)
			             """;

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@hash", promptHash);
			command.Parameters.AddWithValue("@name", promptName);
			command.Parameters.AddWithValue("@content", promptContent);
			command.Parameters.AddWithValue("@now", now);

			await command.ExecuteNonQueryAsync();

			return promptHash;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error storing prompt {PromptName}", promptName);
			throw;
		}
	}

	private static string GeneratePromptHash(string promptContent) {
		using SHA256 sha256    = SHA256.Create();
		byte[]       hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(promptContent));
		return Convert.ToHexString(hashBytes).ToLowerInvariant();
	}

	public async Task<(string Name, string Content)?> GetPromptAsync(string promptHash) {
		try {
			string sql = "SELECT name, content FROM prompts WHERE hash = @hash";

			await using SqliteCommand command = new SqliteCommand(sql, _con);
			command.Parameters.AddWithValue("@hash", promptHash);

			await using SqliteDataReader reader = await command.ExecuteReaderAsync();

			if (await reader.ReadAsync()) {
				return (reader.GetString(0), reader.GetString(1));
			}

			return null;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting prompt for hash {Hash}", promptHash);
			return null;
		}
	}

	/// <summary>
	/// Returns all cache entries with metadata for analysis where LEFT JOIN includes prompt
	/// details where ordering by last-accessed shows hot entries where comprehensive metadata
	/// enables cache effectiveness analysis and debugging
	/// </summary>
	public async Task<List<CacheEntryInfo>> GetAllEntriesAsync() {
		try {
			string sql = """
			             SELECT ce.key, ce.type_name, ce.value, ce.created_at, ce.expires_at, ce.last_accessed, 
			                    ce.prompt_name, p.name as prompt_display_name, ce.model_name, ce.provider_name
			             FROM cache_entries ce
			             LEFT JOIN prompts p ON ce.prompt_hash = p.hash
			             ORDER BY ce.last_accessed DESC
			             """;

			await using SqliteCommand    command = new SqliteCommand(sql, _con);
			await using SqliteDataReader reader  = await command.ExecuteReaderAsync();

			List<CacheEntryInfo> entries = new List<CacheEntryInfo>();

			while (await reader.ReadAsync()) {
				entries.Add(new CacheEntryInfo {
					Key               = reader.GetString(0),
					TypeName          = reader.GetString(1),
					Value             = reader.GetString(2),
					CreatedAt         = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)),
					ExpiresAt         = reader.IsDBNull(4) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)),
					LastAccessed      = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
					PromptName        = reader.IsDBNull(6) ? null : reader.GetString(6),
					PromptDisplayName = reader.IsDBNull(7) ? null : reader.GetString(7),
					ModelName         = reader.IsDBNull(8) ? null : reader.GetString(8),
					ProviderName      = reader.IsDBNull(9) ? null : reader.GetString(9)
				});
			}

			return entries;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting all cache entries");
			return new List<CacheEntryInfo>();
		}
	}

	public void Dispose() {
		try {
			_con?.Close();
			_con?.Dispose();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error disposing cache service");
		}
	}

	private async Task<List<CachedOptimization>> GetCachedOptimizations(string? pattern) {
		List<CachedOptimization> results = [];

		// This is a bit hacky since SqliteCacheService doesn't expose a query method
		// We'll need to add this functionality
		string dbpath = GLB.CacheDbPath;
		if (!File.Exists(dbpath)) return results;

		await using SqliteConnection con = new SqliteConnection($"Data Source={dbpath}");
		await con.OpenAsync();

		string query = """
		               SELECT key, value 
		               FROM cache_entries 
		               WHERE key LIKE 'optimization_%' 
		               ORDER BY key
		               """;

		if (!string.IsNullOrEmpty(pattern)) {
			query += $" AND (key LIKE '%{pattern}%' OR value LIKE '%{pattern}%')";
		}

		await using SqliteCommand    cmd    = new SqliteCommand(query, con);
		await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();

		while (await reader.ReadAsync()) {
			string key   = reader.GetString(0);
			string value = reader.GetString(1);

			// Parse key: optimization_{symbolName}_{filePath}_{line}_{level}
			string[] parts = key.Split('_', 4);
			if (parts.Length >= 4) {
				results.Add(new CachedOptimization {
					SymbolName  = parts[1],
					FilePath    = parts[2],
					Line        = parts.Length > 3 && int.TryParse(parts[3], out int line) ? line : 0,
					Compression = value.Trim('"')
				});
			}
		}

		return results;
	}

	private async Task<List<CachedKey>> GetCachedKeys() {
		List<CachedKey> results = [];

		string cacheDbPath = GLB.CacheDbPath;
		if (!File.Exists(cacheDbPath)) return results;

		await using SqliteConnection con = new SqliteConnection($"Data Source={cacheDbPath}");
		await con.OpenAsync();

		const string QUERY = @"
            SELECT key, value 
            FROM cache_entries 
            WHERE key LIKE 'key_L%' 
            ORDER BY key";

		await using SqliteCommand    cmd    = new SqliteCommand(QUERY, con);
		await using SqliteDataReader reader = await cmd.ExecuteReaderAsync();

		while (await reader.ReadAsync()) {
			string key   = reader.GetString(0);
			string value = reader.GetString(1);

			// Parse key: key_L{level}_{hash}
			if (key.StartsWith("key_L")) {
				string levelStr = key.Substring(5, 1);
				if (int.TryParse(levelStr, out int level)) {
					results.Add(new CachedKey {
						Level   = level,
						Pattern = value.Trim('"')
					});
				}
			}
		}

		return results;
	}
}