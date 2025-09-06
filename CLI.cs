using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Reflection;
using Thaum.CLI.Models;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using static System.Console;
using static Thaum.Core.Utils.TraceLogger;
using System.Diagnostics.CodeAnalysis;
using Thaum.Core;
using Thaum.Utils;

namespace Thaum.CLI;

/// <summary>
/// Command-line interface orchestrator using partial class pattern for modular organization
/// where each command lives in separate file maintaining single responsibility where ambient
/// services eliminate constructor ceremony where perceptual coloring creates semantic visual
/// feedback where trace logging enables debugging through consciousness stream observation
/// </summary>
public partial class CLI {
	private readonly LLM               _llm;
	private readonly ILogger<CLI>      _logger;
	private readonly PerceptualColorer _colorer;
	private readonly CodeCrawler       _crawler;
	private readonly Prompter          _prompter;
	private readonly Compressor        _compressor;
	// private readonly TryCommands       _try;

	private record CachedOptimization {
		public string         SymbolName   { get; init; } = "";
		public string         FilePath     { get; init; } = "";
		public int            Line         { get; init; }
		public string         Compression  { get; init; } = "";
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

	/// <summary>
	/// Initializes CLI with ambient services where HttpLLM provides AI capabilities where
	/// TreeSitterCrawler enables code analysis where PerceptualColorer creates visual semantics
	/// where EnvLoader enables hierarchical config where trace logging captures execution flow
	/// </summary>
	public CLI() {
		HttpClient httpClient = new HttpClient();

		_crawler       = new TreeSitterCrawler();
		_logger   = Logging.For<CLI>();
		_colorer  = new PerceptualColorer();
		_llm      = new HttpLLM(httpClient, GLB.AppConfig);
		_prompter = new Prompter(_llm);

		// Initialize trace logger
		Initialize(_logger);
		tracein();

		// Load .env files from directory hierarchy
		EnvLoader.LoadAndApply();

		// Setup configuration for LLM provider
		Cache        cache        = new Cache(GLB.AppConfig);
		PromptLoader promptLoader = new PromptLoader();

		_compressor = new Compressor(_llm, _crawler, cache, promptLoader);
		traceout();
	}

	/// <summary>
	/// Main entry point routing commands to handlers where switch expression enables clean
	/// dispatch where CMD_ prefix groups command methods where trace scope tracks execution
	/// boundaries where help appears for empty args guiding user discovery
	/// </summary>
	public async Task RunAsync(string[] args) {
		tracein(parameters: new { args = string.Join(" ", args) });

		using var scope = trace_scope("RunAsync");

		if (args.Length == 0) {
			trace("No arguments provided, showing help");
			CMD_help();
			traceout();
			return;
		}

		string command = args[0].ToLowerInvariant();
		trace($"Processing command: {command}");

		switch (command) {
			case "ls":
				traceop("Executing ls command");
				await CMD_ls(args);
				break;
			case "ls-env":
				traceop("Executing ls-env command");
				CMD_ls_env(args);
				break;
			case "ls-cache":
				traceop("Executing ls-cache command");
				await CMD_ls_cache(args);
				break;
			case "ls-lsp":
				traceop("Executing ls-lsp command");
				await CMD_try_lsp(args);
				break;
			case "try":
				traceop("Executing try command");
				await CMD_try(args);
				break;
			case "optimize":
				traceop("Executing optimize command");
				await CMD_optimize(args);
				break;
			case "help":
			case "--help":
			case "-h":
				trace("Help command requested, showing help");
				CMD_help();
				break;
			default:
				trace($"Unknown command received: {command}");
				WriteLine($"Unknown command: {command}");
				CMD_help();
				Environment.Exit(1);
				break;
		}

		traceout();
	}

	[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
	private async Task CMD_ls(string[] args) {
		LsOptions options = ParseLsOptions(args);

		// First, always try to load assemblies that we reference
		foreach (var asmName in Assembly.GetExecutingAssembly().GetReferencedAssemblies()) {
			try {
				Assembly.Load(asmName);
			} catch { }
		}

		// Check if asking for an assembly by name (e.g., "TreeSitter" or "TreeSitter.DotNet")
		// Try to find the assembly in the current AppDomain first before checking filesystem
		Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault(a => {
				var name = a.GetName().Name;
				return name?.Equals(options.ProjectPath, StringComparison.OrdinalIgnoreCase) == true ||
				       (options.ProjectPath.Equals("TreeSitter.DotNet", StringComparison.OrdinalIgnoreCase) &&
				        name?.Equals("TreeSitter", StringComparison.OrdinalIgnoreCase) == true);
			});

		if (assembly != null) {
			await CMD_ls_assembly(assembly, options);
			return;
		}

		// Check if the path is a DLL/EXE file
		if (File.Exists(options.ProjectPath) &&
		    (options.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		     options.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) {
			Assembly fileAssembly = Assembly.LoadFrom(options.ProjectPath);
			await CMD_ls_assembly(fileAssembly, options);
			return;
		}

		// Also check if it's a DLL/EXE that doesn't exist yet (for better error message)
		if (options.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		    options.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
			WriteLine($"Error: Could not find assembly file: {options.ProjectPath}");
			WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
			return;
		}

		// If we reach here and the path doesn't exist, list available assemblies to help debug
		if (!Directory.Exists(options.ProjectPath) && !File.Exists(options.ProjectPath)) {
			WriteLine($"Path '{options.ProjectPath}' not found.");
			WriteLine("\nAvailable loaded assemblies:");
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				WriteLine($"  - {name}");
			}
			WriteLine("\nTry 'thaum ls <assembly-name>' where <assembly-name> is one of the above.");
			return;
		}

		WriteLine($"Scanning {options.ProjectPath} for {options.Language} symbols...");

		// Get symbols
		List<CodeSymbol> symbols = await _crawler.CrawlDir(options.ProjectPath);

		if (!symbols.Any()) {
			WriteLine("No symbols found.");
			return;
		}

		// Build and display hierarchy
		List<TreeNode> hierarchy = TreeNode.BuildHierarchy(symbols, _colorer);
		TreeNode.DisplayHierarchy(hierarchy, options);

		WriteLine($"\nFound {symbols.Count} symbols total");
	}

	[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetTypes()")]
	private async Task CMD_ls_assembly(Assembly assembly, LsOptions options) {
		WriteLine($"Scanning assembly {assembly.GetName().Name}...");

		try {
			List<CodeSymbol> symbols = [];

			// Extract types from the assembly
			Type[] types = assembly.GetTypes();

			foreach (Type type in types) {
				// Skip compiler-generated types
				if (type.Name.Contains("<") || type.Name.Contains(">"))
					continue;

				// Create a symbol for the type (class/interface/enum)
				SymbolKind typeKind = SymbolKind.Class;
				if (type.IsInterface)
					typeKind = SymbolKind.Interface;
				// Use Class for enums and structs too since we don't have specific kinds for them

				List<CodeSymbol> typeChildren = [];

				// Get methods
				MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				foreach (MethodInfo method in methods) {
					// Skip compiler-generated methods
					if (method.Name.Contains("<") || method.Name.Contains(">") ||
					    method.Name.StartsWith("get_") || method.Name.StartsWith("set_"))
						continue;

					CodeSymbol methodSymbol = new CodeSymbol(
						Name: method.Name,
						Kind: SymbolKind.Method,
						FilePath: assembly.Location,
						StartCodeLoc: new CodeLoc(0, 0),
						EndCodeLoc: new CodeLoc(0, 0)
					);
					typeChildren.Add(methodSymbol);
				}

				// Get properties
				PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				foreach (PropertyInfo property in properties) {
					CodeSymbol propertySymbol = new CodeSymbol(
						Name: property.Name,
						Kind: SymbolKind.Property,
						FilePath: assembly.Location,
						StartCodeLoc: new CodeLoc(0, 0),
						EndCodeLoc: new CodeLoc(0, 0)
					);
					typeChildren.Add(propertySymbol);
				}

				// Get fields
				FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				foreach (FieldInfo field in fields) {
					// Skip compiler-generated fields
					if (field.Name.Contains("<") || field.Name.Contains(">"))
						continue;

					CodeSymbol fieldSymbol = new CodeSymbol(
						Name: field.Name,
						Kind: SymbolKind.Field,
						FilePath: assembly.Location,
						StartCodeLoc: new CodeLoc(0, 0),
						EndCodeLoc: new CodeLoc(0, 0)
					);
					typeChildren.Add(fieldSymbol);
				}

				CodeSymbol typeSymbol = new CodeSymbol(
					Name: type.Name,
					Kind: typeKind,
					FilePath: assembly.Location,
					StartCodeLoc: new CodeLoc(0, 0),
					EndCodeLoc: new CodeLoc(0, 0),
					Children: typeChildren.Any() ? typeChildren : null
				);

				symbols.Add(typeSymbol);
			}

			if (!symbols.Any()) {
				WriteLine("No symbols found in assembly.");
				return;
			}

			// Build and display hierarchy
			List<TreeNode> tree = TreeNode.BuildHierarchy(symbols, _colorer);
			TreeNode.DisplayHierarchy(tree, options);

			WriteLine($"\nFound {symbols.Count} types in assembly");
			WriteLine($"Total symbols: {symbols.Count + symbols.SelectMany(s => s.Children ?? []).Count()}");
		} catch (Exception ex) {
			WriteLine($"Error loading assembly: {ex.Message}");
			_logger.LogError(ex, "Failed to load assembly {AssemblyName}", assembly.GetName().Name);
			Environment.Exit(1);
		}

		await Task.CompletedTask;
	}

	private async Task CMD_optimize(string[] args) {
		SummarizeOptions options = ParseSummarizeOptions(args);

		WriteLine($"Starting hierarchical optimization of {options.ProjectPath} ({options.Language})...");
		WriteLine();

		try {
			DateTime        startTime = DateTime.UtcNow;
			SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(options.ProjectPath, options.Language, options.CompressionLevel);
			TimeSpan        duration  = DateTime.UtcNow - startTime;

			// Display extracted keys
			TraceLogger.traceheader("EXTRACTED KEYS");
			foreach (KeyValuePair<string, string> key in hierarchy.ExtractedKeys) {
				TraceLogger.traceln(key.Key, key.Value.Length > 80 ? $"{key.Value[..77]}..." : key.Value, "KEY");
			}

			TraceLogger.traceheader("OPTIMIZATION COMPLETE");
			TraceLogger.traceln("Duration", $"{duration.TotalSeconds:F2} seconds", "TIME");
			TraceLogger.traceln("Root Symbols", $"{hierarchy.RootSymbols.Count} symbols", "COUNT");
			TraceLogger.traceln("Keys Generated", $"{hierarchy.ExtractedKeys.Count} keys", "COUNT");

			WriteLine();
			WriteLine("Hierarchical optimization completed successfully!");
		} catch (Exception ex) {
			WriteLine($"Error during optimization: {ex.Message}");
			_logger.LogError(ex, "Optimization failed");
			Environment.Exit(1);
		}
	}

	private static void CMD_ls_env(string[] args) {
		bool showValues = args.Contains("--values") || args.Contains("-v");

		WriteLine("Environment file detection and loading trace:");
		WriteLine();

		EnvLoader.EnvLoadResult result = EnvLoader.LoadEnvironmentFiles();
		EnvLoader.PrintLoadTrace(result, showValues);

		WriteLine();
		WriteLine($"Environment variables successfully loaded and available for configuration.");
	}

	private async Task CMD_ls_cache(string[] args) {
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
		WriteLine("🔍 Thaum Cache Browser - Hierarchical Compressed Symbol Representations");
		ResetColor();
		ForegroundColor = ConsoleColor.DarkCyan;
		WriteLine(new string('═', consoleWidth));
		ResetColor();
		WriteLine();

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
				WriteLine($"📦 CACHED OPTIMIZATIONS ({optimizations.Count} symbols)");
				ResetColor();
				WriteLine();

				// Group by file path for hierarchical organization
				List<IGrouping<string, CachedOptimization>> groupedByFile = optimizations
					.GroupBy(x => Path.GetRelativePath(Directory.GetCurrentDirectory(), x.FilePath))
					.OrderBy(g => g.Key)
					.ToList();

				foreach (IGrouping<string, CachedOptimization> fileGroup in groupedByFile) {
					// File header with color
					ForegroundColor = ConsoleColor.Blue;
					WriteLine($"📁 {fileGroup.Key}");
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
							WriteLine($"{opt.Compression[..(remainingSpace - 3)]}...");
						} else {
							WriteLine(opt.Compression);
						}
						ResetColor();
					}
					WriteLine(); // Space between files
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
					WriteLine(new string('═', consoleWidth));
					ForegroundColor = ConsoleColor.Green;
					WriteLine("🔑 EXTRACTED ARCHITECTURAL KEYS");
					ForegroundColor = ConsoleColor.DarkCyan;
					WriteLine(new string('═', consoleWidth));
					ResetColor();
					WriteLine();

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
							WriteLine($"{key.Pattern[..(remainingSpace - 3)]}...");
						} else {
							WriteLine(key.Pattern);
						}
						ResetColor();
					}
					WriteLine();
				}
			}

			if (!optimizations.Any() && !showKeys) {
				ForegroundColor = ConsoleColor.DarkYellow;
				WriteLine("⚠️  No cached optimizations found. Run 'summarize' first to populate cache.");
				WriteLine("💡 Example: thaum summarize --compression endgame");
				ResetColor();
			}
		} catch (Exception ex) {
			ForegroundColor = ConsoleColor.Red;
			WriteLine($"❌ Error reading cache: {ex.Message}");
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

	private LsOptions ParseLsOptions(string[] args) {
		string projectPath = Directory.GetCurrentDirectory();
		string language    = "auto";
		int    maxDepth    = 10;
		bool   showTypes   = false;
		bool   noColors    = false;

		// Check if first arg is a path/assembly specifier
		if (args.Length > 1 && !args[1].StartsWith("--")) {
			projectPath = args[1];
		}

		for (int i = 1; i < args.Length; i++) {
			switch (args[i]) {
				case "--path" when i + 1 < args.Length:
					projectPath = args[++i];
					break;
				case "--lang" when i + 1 < args.Length:
					language = args[++i];
					break;
				case "--depth" when i + 1 < args.Length:
					maxDepth = int.Parse(args[++i]);
					break;
				case "--types":
					showTypes = true;
					break;
				case "--no-colors":
					noColors = true;
					break;
				default:
					// Skip non-flag args that aren't the first positional argument
					break;
			}
		}

		// Auto-detect language if not specified (skip for assembly inspection and non-existent paths)
		if (language == "auto" &&
		    !projectPath.StartsWith("assembly:", StringComparison.OrdinalIgnoreCase) &&
		    Directory.Exists(projectPath)) {
			language = DetectLanguage(projectPath);
		}

		return new LsOptions(projectPath, language, maxDepth, showTypes, noColors);
	}

	private SummarizeOptions ParseSummarizeOptions(string[] args) {
		string           projectPath      = Directory.GetCurrentDirectory();
		string           language         = "auto";
		CompressionLevel compressionLevel = CompressionLevel.Optimize;

		for (int i = 1; i < args.Length; i++) {
			switch (args[i]) {
				case "--path" when i + 1 < args.Length:
					projectPath = args[++i];
					break;
				case "--lang" when i + 1 < args.Length:
					language = args[++i];
					break;
				case "--compression" when i + 1 < args.Length:
				case "-c" when i + 1 < args.Length:
					string compressionArg = args[++i].ToLowerInvariant();
					compressionLevel = compressionArg switch {
						"optimize" or "o" => CompressionLevel.Optimize,
						"compress" or "c" => CompressionLevel.Compress,
						"golf" or "g"     => CompressionLevel.Golf,
						"endgame" or "e"  => CompressionLevel.Endgame,
						_                 => throw new ArgumentException($"Invalid compression level: {compressionArg}. Valid options: optimize, compress, golf, endgame")
					};
					break;
				case "--endgame":
					compressionLevel = CompressionLevel.Endgame;
					break;
				default:
					if (!args[i].StartsWith("--")) {
						projectPath = args[i];
					}
					break;
			}
		}

		if (language == "auto") {
			language = DetectLanguage(projectPath);
		}

		return new SummarizeOptions(projectPath, language, compressionLevel);
	}

	private string DetectLanguage(string projectPath) {
		string[]             files      = Directory.GetFiles(projectPath, "*.*", SearchOption.TopDirectoryOnly);
		IEnumerable<string?> extensions = files.Select(Path.GetExtension).Where(ext => !string.IsNullOrEmpty(ext));

		Dictionary<string, int> counts = extensions
			.GroupBy(ext => ext.ToLowerInvariant())
			.ToDictionary(g => g.Key, g => g.Count());

		// Check for specific project files first
		if (File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
		    File.Exists(Path.Combine(projectPath, "requirements.txt")) ||
		    counts.GetValueOrDefault(".py", 0) > 0)
			return "python";

		if (File.Exists(Path.Combine(projectPath, "*.csproj")) ||
		    File.Exists(Path.Combine(projectPath, "*.sln")) ||
		    counts.GetValueOrDefault(".cs", 0) > 0)
			return "c-sharp";

		if (File.Exists(Path.Combine(projectPath, "package.json"))) {
			return counts.GetValueOrDefault(".ts", 0) > counts.GetValueOrDefault(".js", 0) ? "typescript" : "javascript";
		}

		if (File.Exists(Path.Combine(projectPath, "Cargo.toml")))
			return "rust";

		if (File.Exists(Path.Combine(projectPath, "go.mod")))
			return "go";

		// Fallback to most common extension
		KeyValuePair<string, int> mostCommon = counts.OrderByDescending(kv => kv.Value).FirstOrDefault();
		return mostCommon.Key switch {
			".py" => "python",
			".cs" => "c-sharp",
			".js" => "javascript",
			".ts" => "typescript",
			".rs" => "rust",
			".go" => "go",
			_     => "python" // Default fallback
		};
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


	// REMOVED: Moved to HierarchyNode.GetKindDisplayName

	private string GetBackgroundColor(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "\e[48;2;70;130;180m",  // Steel Blue
			SymbolKind.Method    => "\e[48;2;100;149;237m", // Cornflower Blue
			SymbolKind.Class     => "\e[48;2;135;206;235m", // Sky Blue
			SymbolKind.Interface => "\e[48;2;173;216;230m", // Light Blue
			SymbolKind.Module    => "\e[48;2;176;196;222m", // Light Steel Blue
			SymbolKind.Namespace => "\e[48;2;191;239;255m", // Alice Blue
			_                    => "\e[48;2;220;220;220m"  // Light Gray
		};
	}

	private void CMD_help() {
		WriteLine("Thaum - Hierarchical Compression Engine");
		WriteLine();
		WriteLine("Usage:");
		WriteLine("  thaum <command> [options]");
		WriteLine();
		WriteLine("Commands:");
		WriteLine("  ls [path]              List symbols in hierarchical format");
		WriteLine("  ls assembly:<name>     List symbols in loaded assembly (e.g., 'ls assembly:TreeSitter')");
		WriteLine("  ls-env [--values]      Show .env file detection and merging trace");
		WriteLine("  ls-cache [pattern]     Browse cached symbol compressions");
		WriteLine("  ls-lsp [--all] [--cleanup]  Manage auto-downloaded LSP servers");
		WriteLine("  try <file> <symbol>    Test prompts on individual symbols");
		WriteLine("  optimize [path]        Generate codebase optimizations");
		WriteLine("  help                   Show this help message");
		WriteLine();
		WriteLine("Options for 'ls':");
		WriteLine("  --path <path>          Project path (default: current directory)");
		WriteLine("  --lang <language>      Language (python, csharp, javascript, etc.)");
		WriteLine("  --depth <number>       Maximum nesting depth (default: 10)");
		WriteLine("  --types                Show symbol types");
		WriteLine("  --no-colors            Disable colored output");
		WriteLine();
		WriteLine("Options for 'ls-env':");
		WriteLine("  --values, -v           Show actual environment variable values");
		WriteLine();
		WriteLine("Options for 'ls-cache':");
		WriteLine("  [pattern]              Filter cached symbols by pattern");
		WriteLine("  --keys, -k             Show K1/K2 architectural keys");
		WriteLine("  --all, -a              Show both optimizations and keys");
		WriteLine();
		WriteLine("Options for 'ls-lsp':");
		WriteLine("  --all, -a              Show detailed information about cached servers");
		WriteLine("  --cleanup, -c          Remove old LSP server versions");
		WriteLine();
		WriteLine("Options for 'try':");
		WriteLine("  <file_path>            Path to source file");
		WriteLine("  <symbol_name>          Name of symbol to test");
		WriteLine("  --prompt <name>        Prompt file name (e.g., compress_function_v2, endgame_function)");
		WriteLine("  --interactive          Launch interactive TUI with live updates");
		WriteLine();
		WriteLine("Options for 'optimize':");
		WriteLine("  --path <path>          Project path (default: current directory)");
		WriteLine("  --lang <language>      Language (python, csharp, javascript, etc.)");
		WriteLine("  --compression <level>  Compression level: optimize, compress, golf, endgame");
		WriteLine("  -c <level>             Short form of --compression");
		WriteLine("  --endgame              Use maximum endgame compression");
		WriteLine();
		WriteLine("Environment Variables:");
		WriteLine("  THAUM_DEFAULT_FUNCTION_PROMPT  Default prompt for functions (default: compress_function_v2)");
		WriteLine("  THAUM_DEFAULT_CLASS_PROMPT     Default prompt for classes (default: compress_class)");
		WriteLine("  LLM__DefaultModel              LLM model to use for compression");
		WriteLine();
		WriteLine("Available Prompts:");
		WriteLine("  optimize_function, optimize_class, optimize_key");
		WriteLine("  compress_function, compress_function_v2, compress_class, compress_key");
		WriteLine("  golf_function, golf_class, golf_key");
		WriteLine("  endgame_function, endgame_class, endgame_key");
		WriteLine();
		WriteLine("Examples:");
		WriteLine("  thaum ls");
		WriteLine("  thaum ls /path/to/project --lang python --depth 3");
		WriteLine("  thaum ls-cache");
		WriteLine("  thaum ls-cache Handle --keys");
		WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy");
		WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt endgame_function");
		WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --interactive");
		WriteLine("  thaum optimize --compression endgame");
		WriteLine("  thaum optimize /path/to/project -c golf");
		WriteLine("  thaum ls-env --values");
		WriteLine();
		WriteLine("Environment Variable Examples:");
		WriteLine("  export THAUM_DEFAULT_FUNCTION_PROMPT=endgame_function");
		WriteLine("  export THAUM_DEFAULT_CLASS_PROMPT=golf_class");
		WriteLine();
		WriteLine("Run without arguments to launch the interactive TUI.");
	}
}