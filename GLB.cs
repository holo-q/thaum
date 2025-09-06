using Microsoft.Extensions.Configuration;
using Thaum.Core.Models;
using Thaum.Core.Services;

/// <summary>
/// Global ambient utilities providing application-wide singleton resources through static access
/// where dependency injection ceremony creates more complexity than it solves where truly global
/// concerns deserve immediate availability without constructor pollution where ambient patterns
/// eliminate parameter explosion for cross-cutting infrastructure needs
/// </summary>
public static class GLB {
	/// <summary>
	/// Default model for LLM operations where environment variable LLM__DefaultModel provides
	/// override capability where fallback ensures reasonable default when environment unconfigured
	/// </summary>
	public static string DefaultModel => Environment.GetEnvironmentVariable("LLM__DefaultModel") ?? "moonshotai/kimi-2";

	/// <summary>
	/// Provides default LLM options for compression operations where temperature 0.3 maintains consistency while allowing creativity
	/// where max tokens 1024 balances completeness with efficiency
	/// where these defaults emerge from empirical tuning rather than arbitrary choice
	/// </summary>
	public static LLMOptions Compress_Defaults(string model) {
		LLMOptions llmOpts = new LLMOptions(
			Temperature: 0.3,
			MaxTokens: 1024,
			Model: model);

		return llmOpts;
	}

	/// <summary>
	/// Ambient configuration access eliminating ceremonial injection patterns where truly global
	/// configuration deserves global access where the pattern recognizes that application config
	/// is genuinely singular where fighting this with DI creates friction without benefit
	/// </summary>
	public static IConfigurationRoot AppConfig => new ConfigurationBuilder()
		.SetBasePath(Directory.GetCurrentDirectory())
		.AddJsonFile("appsettings.json", optional: true)
		.AddEnvironmentVariables()
		.Build();

	/// <summary>
	/// Determines default prompt based on symbol type with environment variable override capability
	/// where THAUM_DEFAULT_FUNCTION_PROMPT and THAUM_DEFAULT_CLASS_PROMPT provide customization
	/// where fallback ensures reasonable defaults when environment is unconfigured
	/// </summary>
	public static string GetDefaultPromptFromEnvironment(CodeSymbol symbol) {
		string symbolType = symbol.Kind switch {
			SymbolKind.Function or SymbolKind.Method => "function",
			SymbolKind.Class                         => "class",
			_                                        => "function"
		};

		// Check environment variables for default prompts
		string  envVarName    = $"THAUM_DEFAULT_{symbolType.ToUpper()}_PROMPT";
		string? defaultPrompt = Environment.GetEnvironmentVariable(envVarName);

		if (!string.IsNullOrEmpty(defaultPrompt)) {
			return defaultPrompt;
		}

		// Fallback to compress_function_v2 for functions, compress_class for classes
		return symbolType == "function" ? "compress_function_v2" : "compress_class"; // TODO this is hardcoded and shouldn't be
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
}