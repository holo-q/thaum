using Microsoft.Extensions.Configuration;
using Thaum.Core.Models;
using Thaum.Core.Services;
using System.Text.Json;

/// <summary>
/// Global ambient utilities providing application-wide singleton resources through static access
/// where dependency injection ceremony creates more complexity than it solves where truly global
/// concerns deserve immediate availability without constructor pollution where ambient patterns
/// eliminate parameter explosion for cross-cutting infrastructure needs
/// </summary>
public static class GLB {
	// Default temperatures for different LLM operations where compression uses 0.3 for consistency
	// where key extraction uses 0.2 for precision where general operations default to 0.7
	public static double CompressTemp => 0.3;
	public static double KeyTemp      => 0.2;
	public static double DefaultTemp  => 0.7;

	// Default token limits for different operations where compression gets 1024 for completeness
	// where key extraction gets 512 for brevity where these values emerge from empirical tuning
	public static int CompressTokens => 1024;
	public static int KeyTokens      => 512;

	/// <summary>
	/// Default model for LLM operations where environment variable LLM__DefaultModel provides
	/// override capability where fallback ensures reasonable default when environment unconfigured
	/// </summary>
	public static string DefaultModel => Environment.GetEnvironmentVariable("LLM__DefaultModel") ?? "moonshotai/kimi-2";


	/// <summary>
	/// Ambient configuration access eliminating ceremonial injection patterns where truly global
	/// configuration deserves global access where the pattern recognizes that application config
	/// is genuinely singular where fighting this with DI creates friction without benefit
	/// </summary>
	public static IConfigurationRoot AppConfig => new ConfigurationBuilder()
		.SetBasePath(Directory.GetCurrentDirectory())
		.AddJsonFile(AppSettingsFile, optional: true)
		.AddEnvironmentVariables()
		.Build();

	/// <summary>
	/// Default prompt names for different symbol types where functions use compress_function_v5
	/// where classes use compress_class where these can be overridden via environment variables
	/// </summary>
	public static string DefaultFunctionPrompt => Environment.GetEnvironmentVariable("THAUM_DEFAULT_FUNCTION_PROMPT") ?? "compress_function_v5";

	public static string DefaultClassPrompt => Environment.GetEnvironmentVariable("THAUM_DEFAULT_CLASS_PROMPT") ?? "compress_class";
	public static string DefaultKeyPrompt   => Environment.GetEnvironmentVariable("THAUM_DEFAULT_KEY_PROMPT") ?? "compress_key";

	/// <summary>
	/// Gets default prompt name for symbol type with environment override capability
	/// where specific symbol overrides can be specified or falls back to type defaults
	/// </summary>
	public static string GetDefaultPrompt(CodeSymbol symbol) {
		string symbolType = symbol.Kind switch {
			SymbolKind.Function or SymbolKind.Method => "function",
			SymbolKind.Class                         => "class",
			_                                        => "function"
		};

		// Check for specific symbol override first
		string  envVarName    = $"THAUM_DEFAULT_{symbolType.ToUpper()}_PROMPT";
		string? defaultPrompt = Environment.GetEnvironmentVariable(envVarName);

		if (!string.IsNullOrEmpty(defaultPrompt)) {
			return defaultPrompt;
		}

		// Use type defaults
		return symbolType == "function" ? DefaultFunctionPrompt : DefaultClassPrompt;
	}

	/// <summary>
	/// Gets prompt name with environment variable override support where specific prompt names
	/// can be overridden per symbol type while maintaining sensible defaults
	/// </summary>
	public static string GetPromptName(string basePromptName, string symbolType) {
		string envVarName = $"THAUM_PROMPT_{basePromptName.ToUpper()}_{symbolType.ToUpper()}";
		return Environment.GetEnvironmentVariable(envVarName) ?? $"{basePromptName}_{symbolType}";
	}

	/// <summary>
	/// Maps symbol names to semantic icons through pattern recognition where naming conventions reveal intent
	/// where Async gets lightning, where Get reads, where Set writes, where Handle controls
	/// where each icon creates instant visual recognition of method purpose
	/// </summary>
	public static string GetSymbolTypeIcon(string symbolName) {
		// Simple heuristics to determine symbol type
		if (symbolName.EndsWith("Async")) return "⚡";
		if (symbolName.StartsWith("Get")) return "📖";
		if (symbolName.StartsWith("Set") || symbolName.StartsWith("Update")) return "✏️";
		if (symbolName.StartsWith("Handle")) return "🎛️";
		if (symbolName.StartsWith("Build") || symbolName.StartsWith("Create")) return "🔨";
		if (symbolName.StartsWith("Load") || symbolName.StartsWith("Read")) return "📥";
		if (symbolName.StartsWith("Save") || symbolName.StartsWith("Write")) return "💾";
		if (symbolName.Contains("Dispose")) return "🗑️";
		return "🔧";
	}

	/// <summary>
	/// Standard directories where prompts provides template directory where cache provides storage
	/// where these paths follow platform conventions while maintaining consistency
	/// </summary>
	public static string PromptsDir => Path.Combine(Directory.GetCurrentDirectory(), "prompts");

	public static string CacheDir    => AppConfig["Cache:Directory"] ?? Path.Combine(Path.GetTempPath(), "Thaum");
	public static string CacheDbPath => Path.Combine(CacheDir, "cache.db");

	/// <summary>
	/// Standard filenames where interactive log captures TUI sessions where output log handles general logging
	/// where env file provides environment configuration where app settings provides JSON configuration
	/// </summary>
	public static string InteractiveLogFile => "interactive.log";

	public static string OutputLogFile   => "output.log";
	public static string EnvFileName     => ".env";
	public static string AppSettingsFile => "appsettings.json";

	/// <summary>
	/// LLM provider API keys with environment variable access where each provider gets dedicated key access
	/// where fallback to generic LLM:ApiKey maintains backward compatibility
	/// </summary>
	public static string? API_KEY_OPENAI => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? AppConfig["LLM:ApiKey"];

	public static string? ANTHROPIC_AIP_KEY  => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? AppConfig["LLM:ApiKey"];
	public static string? OPENROUTER_API_KEY => Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? AppConfig["LLM:ApiKey"];

	/// <summary>
	/// Standard timeouts and limits where console width ensures readable output where OSC timeout
	/// prevents hanging on color detection where buffer size optimizes file operations
	/// </summary>
	public static int ConsoleMinWidth => 120;

	public static int OscTimeoutMs   => 1000;
	public static int FileBufferSize => 8192;

	/// <summary>
	/// JSON serialization options for cache operations where camel case follows conventions
	/// where null omission reduces storage where these options maintain consistency
	/// </summary>
	public static JsonSerializerOptions JsonOptions => new JsonSerializerOptions {
		PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
		WriteIndented          = false,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// JSON serialization options for cache with type info resolver where JsonContext provides
	/// type metadata for efficient serialization where cached serialization options
	/// maintain consistency across cache operations
	/// </summary>
	public static JsonSerializerOptions CacheJsonOptions => new JsonSerializerOptions {
		PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
		WriteIndented          = false,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
		TypeInfoResolver       = JsonContext.Default
	};

	/// <summary>
	/// Creates LLM options for compression operations using standard defaults where model override
	/// enables provider selection where consistent options ensure reproducible results
	/// </summary>
	public static LLMOptions CompressionOptions(string? model = null) => new LLMOptions(
		Temperature: CompressTemp,
		MaxTokens: CompressTokens,
		Model: model ?? DefaultModel
	);

	/// <summary>
	/// Creates LLM options for key extraction using precision defaults where lower temperature
	/// ensures consistent key generation where reduced tokens focus on essential elements
	/// </summary>
	public static LLMOptions KeyOptions(string? model = null) => new LLMOptions(
		Temperature: KeyTemp,
		MaxTokens: KeyTokens,
		Model: model ?? DefaultModel
	);
}