using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using Thaum.Core;
using Thaum.Core.Models;
using Thaum.Core.Services;
using static Thaum.Core.Utils.Tracer;
using Thaum.Core.Utils;
using Thaum.Utils;
using static System.Console;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing ls-cache command implementation where cached compressions
/// display with semantic coloring where K1/K2 keys reveal architectural patterns where
/// prompt metadata tracks LLM model usage where compression history enables analysis
/// </summary>
public partial class CLI {
	// Cache entry record types
	private record CachedOptimization {
		public string         SymbolName   { get; init; } = "";
		public string         FilePath     { get; init; } = "";
		public int            Line         { get; init; }
		public string         Compression  { get; init; } = "";
		public string         Pattern      { get; init; } = "";
		public string?        PromptName   { get; init; }
		public string?        ModelName    { get; init; }
		public string?        ProviderName { get; init; }
		public DateTimeOffset CreatedAt    { get; init; }
		public DateTimeOffset LastAccessed { get; init; }
	}

	private record CachedKey {
		public int            Level        { get; init; }
		public string         Pattern      { get; init; } = "";
		public string?        PromptName   { get; init; }
		public string?        ModelName    { get; init; }
		public string?        ProviderName { get; init; }
		public DateTimeOffset CreatedAt    { get; init; }
		public DateTimeOffset LastAccessed { get; init; }
	}

	public async Task CMD_ls_cache(string[] args) {
		bool   showKeys    = args.Contains("--keys") || args.Contains("-k");
		bool   showAll     = args.Contains("--all") || args.Contains("-a");
		bool   showDetails = args.Contains("--details") || args.Contains("-d");
		string pattern     = GetPatternFromArgs(args);

		string GetPatternFromArgs(string[] args) {
			for (int i = 1; i < args.Length; i++) {
				if (!args[i].StartsWith("-") && i > 0 && !args[i - 1].StartsWith("-")) {
					return args[i];
				}
			}
			return string.Empty;
		}

		// Get console width for full horizontal space usage
		int consoleWidth = Math.Max(WindowWidth, 120);

		// Header with colors
		ForegroundColor = ConsoleColor.Cyan;
		println("🔍 Thaum Cache Browser - Hierarchical Compressed Symbol Representations");
		ResetColor();
		ForegroundColor = ConsoleColor.DarkCyan;
		println(new string('═', consoleWidth));
		ResetColor();
		println();

		try {
			Cache cache = new Cache(GLB.AppConfig);

			// Get all cache entries with prompt metadata
			List<CacheEntryInfo> allEntries = await cache.GetAllEntriesAsync();
			List<CachedOptimization> optimizations = allEntries
				.Where(e => e.Key.StartsWith("optimization_"))
				.Where(e => string.IsNullOrEmpty(pattern) || e.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				.Select(ParseOptimizationEntry)
				.Where(e => e != null)
				.Cast<CachedOptimization>()
				.ToList();

			if (optimizations.Any()) {
				ForegroundColor = ConsoleColor.Green;
				println($"📦 CACHED OPTIMIZATIONS ({optimizations.Count} symbols)");
				ResetColor();
				println();

				// Group by file path for hierarchical organization
				List<IGrouping<string, CachedOptimization>> groupedByFile = optimizations
					.GroupBy(x => Path.GetRelativePath(Directory.GetCurrentDirectory(), x.FilePath))
					.OrderBy(g => g.Key)
					.ToList();

				foreach (IGrouping<string, CachedOptimization> fileGroup in groupedByFile) {
					// File header with color
					ForegroundColor = ConsoleColor.Blue;
					println($"📁 {fileGroup.Key}");
					ResetColor();

					// One line per symbol with compression, prompt info, and model info
					List<CachedOptimization> fileSymbols = fileGroup.OrderBy(x => x.SymbolName).ToList();
					foreach (CachedOptimization opt in fileSymbols) {
						string icon          = GLB.GetSymbolTypeIcon(opt.SymbolName);
						string symbolDisplay = $"  {icon} ";

						Write(symbolDisplay);

						// Use background coloring like ls command
						SymbolKind symbolKind = InferSymbolKind(opt.SymbolName);
						if (args.Contains("--no-colors")) {
							Write(opt.SymbolName);
						} else {
							SemanticColorType semanticType = symbolKind switch {
								SymbolKind.Function  => SemanticColorType.Function,
								SymbolKind.Method    => SemanticColorType.Function,
								SymbolKind.Class     => SemanticColorType.Class,
								SymbolKind.Interface => SemanticColorType.Interface,
								SymbolKind.Module    => SemanticColorType.Module,
								SymbolKind.Namespace => SemanticColorType.Namespace,
								_                    => SemanticColorType.Function
							};
							(int r, int g, int b) = _colorer.GenerateSemanticColor(opt.SymbolName, semanticType);
							Write($"\e[48;2;{r};{g};{b}m\e[38;2;0;0;0m{opt.SymbolName}\e[0m");
						}

						if (opt.Line > 0) {
							ForegroundColor = ConsoleColor.DarkGray;
							Write($":{opt.Line}");
							ResetColor();
						}

						// Show metadata in a structured way
						List<string> metadataItems = [];

						// Prompt info
						if (!string.IsNullOrEmpty(opt.PromptName)) {
							ForegroundColor = ConsoleColor.Magenta;
							Write($" [{opt.PromptName}]");
							ResetColor();
						}

						// Model and provider info with enhanced formatting
						if (!string.IsNullOrEmpty(opt.ModelName) || !string.IsNullOrEmpty(opt.ProviderName)) {
							ForegroundColor = ConsoleColor.DarkCyan;
							Write(" (");
							ForegroundColor = ConsoleColor.Cyan;
							Write(opt.ProviderName ?? "unknown");
							ForegroundColor = ConsoleColor.DarkGray;
							Write(":");
							ForegroundColor = ConsoleColor.Yellow;
							Write(opt.ModelName ?? "unknown");
							ForegroundColor = ConsoleColor.DarkCyan;
							Write(")");
							ResetColor();
						}

						// Show timestamp if details requested
						if (showDetails) {
							ForegroundColor = ConsoleColor.DarkGray;
							Write($" ⏰{opt.CreatedAt:MM-dd HH:mm}");
							if (opt.LastAccessed != opt.CreatedAt) {
								Write($" (last: {opt.LastAccessed:MM-dd HH:mm})");
							}
							ResetColor();
						}

						// Calculate remaining space for compression - leave 1 char margin
						string promptInfo = !string.IsNullOrEmpty(opt.PromptName) ? $" [{opt.PromptName}]" : "";
						string modelInfo = !string.IsNullOrEmpty(opt.ModelName) || !string.IsNullOrEmpty(opt.ProviderName)
							? $" ({opt.ProviderName ?? "unknown"}:{opt.ModelName ?? "unknown"})"
							: "";
						string timestampInfo = showDetails ? $" ⏰{opt.CreatedAt:MM-dd HH:mm}" : "";
						if (showDetails && opt.LastAccessed != opt.CreatedAt) {
							timestampInfo += $" (last: {opt.LastAccessed:MM-dd HH:mm})";
						}
						int usedSpace = symbolDisplay.Length + opt.SymbolName.Length +
						                (opt.Line > 0 ? $":{opt.Line}".Length : 0) +
						                promptInfo.Length + modelInfo.Length + timestampInfo.Length + 3; // +3 for " → "
						int remainingSpace = consoleWidth - usedSpace - 1;                               // -1 for margin

						ForegroundColor = ConsoleColor.DarkGray;
						Write(" → ");
						ResetColor();
						ForegroundColor = ConsoleColor.Yellow;

						// Truncate compression if too long, otherwise show full
						if (opt.Compression.Length > remainingSpace) {
							println($"{opt.Compression[..(remainingSpace - 3)]}...");
						} else {
							println(opt.Compression);
						}
						ResetColor();
					}
					println(); // Space between files
				}
			}

			// Show K1/K2 keys if requested or no optimizations found
			if (showKeys || showAll || !optimizations.Any()) {
				List<CachedKey> keyEntries = allEntries
					.Where(e => e.Key.StartsWith("key_L"))
					.Select(e => ParseKeyEntry(e))
					.Where(e => e != null)
					.Cast<CachedKey>()
					.ToList();

				if (keyEntries.Any()) {
					ForegroundColor = ConsoleColor.DarkCyan;
					println(new string('═', consoleWidth));
					ForegroundColor = ConsoleColor.Green;
					println("🔑 EXTRACTED ARCHITECTURAL KEYS");
					ForegroundColor = ConsoleColor.DarkCyan;
					println(new string('═', consoleWidth));
					ResetColor();
					println();

					foreach (CachedKey key in keyEntries.OrderBy(x => x.Level)) {
						ForegroundColor = ConsoleColor.Green;
						Write($"K{key.Level}");
						ForegroundColor = ConsoleColor.DarkGray;
						Write(" → ");
						ResetColor();

						// Show prompt info if available
						if (!string.IsNullOrEmpty(key.PromptName)) {
							ForegroundColor = ConsoleColor.Magenta;
							Write($"[{key.PromptName}] ");
							ResetColor();
						}

						// Show model and provider info with enhanced formatting
						if (!string.IsNullOrEmpty(key.ModelName) || !string.IsNullOrEmpty(key.ProviderName)) {
							ForegroundColor = ConsoleColor.DarkCyan;
							Write("(");
							ForegroundColor = ConsoleColor.Cyan;
							Write(key.ProviderName ?? "unknown");
							ForegroundColor = ConsoleColor.DarkGray;
							Write(":");
							ForegroundColor = ConsoleColor.Yellow;
							Write(key.ModelName ?? "unknown");
							ForegroundColor = ConsoleColor.DarkCyan;
							Write(") ");
							ResetColor();
						}

						// Show timestamp if details requested
						if (showDetails) {
							ForegroundColor = ConsoleColor.DarkGray;
							Write($"⏰{key.CreatedAt:MM-dd HH:mm} ");
							if (key.LastAccessed != key.CreatedAt) {
								Write($"(last: {key.LastAccessed:MM-dd HH:mm}) ");
							}
							ResetColor();
						}

						ForegroundColor = ConsoleColor.Green;

						// Calculate remaining space for key pattern - leave 1 char margin
						string promptInfo = !string.IsNullOrEmpty(key.PromptName) ? $"[{key.PromptName}] " : "";
						string modelInfo = !string.IsNullOrEmpty(key.ModelName) || !string.IsNullOrEmpty(key.ProviderName)
							? $"({key.ProviderName ?? "unknown"}:{key.ModelName ?? "unknown"}) "
							: "";
						string timestampInfo = showDetails ? $"⏰{key.CreatedAt:MM-dd HH:mm} " : "";
						if (showDetails && key.LastAccessed != key.CreatedAt) {
							timestampInfo += $"(last: {key.LastAccessed:MM-dd HH:mm}) ";
						}
						int usedSpace      = $"K{key.Level} → ".Length + promptInfo.Length + modelInfo.Length + timestampInfo.Length;
						int remainingSpace = consoleWidth - usedSpace - 1; // -1 for margin

						if (key.Pattern.Length > remainingSpace) {
							println($"{key.Pattern[..(remainingSpace - 3)]}...");
						} else {
							println(key.Pattern);
						}
						ResetColor();
					}
					println();
				}
			}

			if (!optimizations.Any() && !showKeys) {
				ForegroundColor = ConsoleColor.DarkYellow;
				println("⚠️  No cached optimizations found. Run 'summarize' first to populate cache.");
				println("💡 Example: thaum summarize --compression endgame");
				ResetColor();
			}
		} catch (Exception ex) {
			ForegroundColor = ConsoleColor.Red;
			println($"❌ Error reading cache: {ex.Message}");
			ResetColor();
			Environment.Exit(1);
		}
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	private static CachedOptimization? ParseOptimizationEntry(CacheEntryInfo entry) {
		try {
			// TODO this is a major problem, all that information should be separated already in entry
			// Parse key format: optimization_{symbolName}_{filePath}_{line}_{level}
			string[] keyParts = entry.Key.Split('_', 3);
			if (keyParts.Length < 3) return null;

			string remainder = keyParts[2]; // Everything after "optimization_"

			int iLastUnderscore = remainder.LastIndexOf('_');
			if (iLastUnderscore == -1)
				return null;

			int secondLastUnderscoreIndex = remainder.LastIndexOf('_', iLastUnderscore - 1);
			if (secondLastUnderscoreIndex == -1) return null;

			string symbolName = remainder[..secondLastUnderscoreIndex];
			string filePath   = remainder[(secondLastUnderscoreIndex + 1)..iLastUnderscore];
			string lineStr    = remainder[(iLastUnderscore + 1)..];

			if (!int.TryParse(lineStr, out int line)) return null;

			// Deserialize the cached value
			string compression = entry.TypeName == "System.String" && JsonSerializer.Deserialize<string>(entry.Value) is { } value
				? value
				: "[Invalid Cache Value]";

			return new CachedOptimization {
				SymbolName   = symbolName,
				FilePath     = filePath,
				Line         = line,
				Compression  = compression,
				PromptName   = entry.PromptDisplayName ?? entry.PromptName,
				ModelName    = entry.ModelName,
				ProviderName = entry.ProviderName,
				CreatedAt    = entry.CreatedAt,
				LastAccessed = entry.LastAccessed
			};
		} catch {
			return null;
		}
	}

	[RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<TValue>(String, JsonSerializerOptions)")]
	private static CachedKey? ParseKeyEntry(CacheEntryInfo entry) {
		try {
			// Parse key format: key_L{level}_{hash}
			string[] keyParts = entry.Key.Split('_');
			if (keyParts.Length < 2 || !keyParts[1].StartsWith('L')) return null;

			string levelStr = keyParts[1][1..]; // Remove 'L' prefix
			if (!int.TryParse(levelStr, out int level)) return null;

			// Deserialize the cached value
			string pattern = entry.TypeName == "System.String" && JsonSerializer.Deserialize<string>(entry.Value) is { } value
				? value
				: "[Invalid Cache Value]";

			return new CachedKey {
				Level        = level,
				Pattern      = pattern,
				PromptName   = entry.PromptDisplayName ?? entry.PromptName,
				ModelName    = entry.ModelName,
				ProviderName = entry.ProviderName,
				CreatedAt    = entry.CreatedAt,
				LastAccessed = entry.LastAccessed
			};
		} catch {
			return null;
		}
	}

	private SymbolKind InferSymbolKind(string symbolName) {
		// Infer symbol kind from naming patterns
		if (symbolName.EndsWith("Async") || symbolName.StartsWith("Get") || symbolName.StartsWith("Set") ||
		    symbolName.StartsWith("Handle") || symbolName.StartsWith("Build") || symbolName.StartsWith("Create") ||
		    symbolName.StartsWith("Load") || symbolName.StartsWith("Save") || symbolName.StartsWith("Update") ||
		    symbolName.StartsWith("Process") || symbolName.StartsWith("Execute") || symbolName.StartsWith("Run") ||
		    symbolName.StartsWith("Start") || symbolName.StartsWith("Stop") || symbolName.Contains("Method")) {
			return SymbolKind.Method;
		}

		if (char.IsUpper(symbolName[0]) && !symbolName.Contains("_") &&
		    (symbolName.EndsWith("Service") || symbolName.EndsWith("Manager") || symbolName.EndsWith("Provider") ||
		     symbolName.EndsWith("Engine") || symbolName.EndsWith("Builder") || symbolName.EndsWith("Factory") ||
		     symbolName.EndsWith("Handler") || symbolName.EndsWith("Controller") || symbolName.EndsWith("View") ||
		     symbolName.EndsWith("Model") || symbolName.EndsWith("Application") || symbolName.EndsWith("Window"))) {
			return SymbolKind.Class;
		}

		if (symbolName.StartsWith("I") && char.IsUpper(symbolName.Length > 1 ? symbolName[1] : 'a')) {
			return SymbolKind.Interface;
		}

		// Default to function for other cases
		return SymbolKind.Function;
	}
}