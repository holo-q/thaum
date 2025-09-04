using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Reflection;
using Serilog;
using Serilog.Extensions.Logging;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using Terminal.Gui;

namespace Thaum.CLI;

public class CliApplication {
	private readonly ILanguageServer               _languageServerManager;
	private readonly ILogger<CliApplication>         _logger;
	private readonly PerceptualColorEngine           _colorEngine;
	private readonly Compressor _summaryEngine;

	public CliApplication(ILogger<CliApplication> logger) {
		ILoggerFactory loggerFactory = new SerilogLoggerFactory();
		_languageServerManager  = new LSTreeSitter(loggerFactory);
		_logger      = logger;
		_colorEngine = new PerceptualColorEngine();

		// Initialize trace logger
		TraceLogger.Initialize(_logger);
		TraceLogger.TraceEnter();

		// Load .env files from directory hierarchy
		EnvLoader.LoadAndApply();

		// Setup configuration for LLM provider
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: true)
			.AddEnvironmentVariables()
			.Build();

		HttpClient httpClient   = new HttpClient();
		HttpLLM llmProvider  = new HttpLLM(httpClient, configuration, loggerFactory.CreateLogger<HttpLLM>());
		Cache cache        = new Cache(configuration, loggerFactory.CreateLogger<Cache>());
		PromptLoader promptLoader = new PromptLoader(loggerFactory.CreateLogger<PromptLoader>());

		_summaryEngine = new Compressor(
			llmProvider,
			_languageServerManager,
			cache,
			promptLoader,
			loggerFactory.CreateLogger<Compressor>()
		);

		TraceLogger.TraceExit();
	}

	public async Task RunAsync(string[] args) {
		TraceLogger.TraceEnter(parameters: new { args = string.Join(" ", args) });

		using var scope = ScopeTracer.TraceScope("RunAsync");

		if (args.Length == 0) {
			TraceLogger.TraceInfo("No arguments provided, showing help");
			ShowHelp();
			TraceLogger.TraceExit();
			return;
		}

		string command = args[0].ToLowerInvariant();
		TraceLogger.TraceInfo($"Processing command: {command}");

		switch (command) {
			case "ls":
				TraceLogger.TraceOperation("Executing ls command");
				await HandleLsCommand(args);
				break;
			case "ls-env":
				TraceLogger.TraceOperation("Executing ls-env command");
				HandleLsEnvCommand(args);
				break;
			case "ls-cache":
				TraceLogger.TraceOperation("Executing ls-cache command");
				await HandleLsCacheCommand(args);
				break;
			case "ls-lsp":
				TraceLogger.TraceOperation("Executing ls-lsp command");
				await HandleLsLspCommand(args);
				break;
			case "try":
				TraceLogger.TraceOperation("Executing try command");
				await HandleTryCommand(args);
				break;
			case "optimize":
				TraceLogger.TraceOperation("Executing optimize command");
				await HandleOptimizeCommand(args);
				break;
			case "help":
			case "--help":
			case "-h":
				TraceLogger.TraceInfo("Help command requested, showing help");
				ShowHelp();
				break;
			default:
				TraceLogger.TraceInfo($"Unknown command received: {command}");
				Console.WriteLine($"Unknown command: {command}");
				ShowHelp();
				Environment.Exit(1);
				break;
		}

		TraceLogger.TraceExit();
	}

	private async Task HandleLsCommand(string[] args) {
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
			await HandleAssemblyListing(assembly, options);
			return;
		}

		// Check if the path is a DLL/EXE file
		if (File.Exists(options.ProjectPath) &&
		    (options.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		     options.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) {
			Assembly fileAssembly = Assembly.LoadFrom(options.ProjectPath);
			await HandleAssemblyListing(fileAssembly, options);
			return;
		}

		// Also check if it's a DLL/EXE that doesn't exist yet (for better error message)
		if (options.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		    options.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
			Console.WriteLine($"Error: Could not find assembly file: {options.ProjectPath}");
			Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
			return;
		}

		// If we reach here and the path doesn't exist, list available assemblies to help debug
		if (!Directory.Exists(options.ProjectPath) && !File.Exists(options.ProjectPath)) {
			Console.WriteLine($"Path '{options.ProjectPath}' not found.");
			Console.WriteLine("\nAvailable loaded assemblies:");
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				Console.WriteLine($"  - {name}");
			}
			Console.WriteLine("\nTry 'thaum ls <assembly-name>' where <assembly-name> is one of the above.");
			return;
		}

		Console.WriteLine($"Scanning {options.ProjectPath} for {options.Language} symbols...");

		try {
			// Start language server
			bool started = await _languageServerManager.StartLanguageServerAsync(options.Language, options.ProjectPath);
			if (!started) {
				Console.WriteLine($"Failed to start {options.Language} language server");
				Environment.Exit(1);
			}

			// Get symbols
			List<CodeSymbol> symbols = await _languageServerManager.GetWorkspaceSymbolsAsync(options.Language, options.ProjectPath);

			if (!symbols.Any()) {
				Console.WriteLine("No symbols found.");
				return;
			}

			// Build and display hierarchy
			List<HierarchyNode> hierarchy = BuildHierarchy(symbols);
			DisplayHierarchy(hierarchy, options);

			Console.WriteLine($"\nFound {symbols.Count} symbols total");
		} finally {
			await _languageServerManager.StopLanguageServerAsync(options.Language);
		}
	}

	private async Task HandleAssemblyListing(Assembly assembly, LsOptions options) {
		Console.WriteLine($"Scanning assembly {assembly.GetName().Name}...");

		try {
			List<CodeSymbol> symbols = new List<CodeSymbol>();

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

				List<CodeSymbol> typeChildren = new List<CodeSymbol>();

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
						StartPosition: new Position(0, 0),
						EndPosition: new Position(0, 0)
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
						StartPosition: new Position(0, 0),
						EndPosition: new Position(0, 0)
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
						StartPosition: new Position(0, 0),
						EndPosition: new Position(0, 0)
					);
					typeChildren.Add(fieldSymbol);
				}

				CodeSymbol typeSymbol = new CodeSymbol(
					Name: type.Name,
					Kind: typeKind,
					FilePath: assembly.Location,
					StartPosition: new Position(0, 0),
					EndPosition: new Position(0, 0),
					Children: typeChildren.Any() ? typeChildren : null
				);

				symbols.Add(typeSymbol);
			}

			if (!symbols.Any()) {
				Console.WriteLine("No symbols found in assembly.");
				return;
			}

			// Build and display hierarchy
			List<HierarchyNode> hierarchy = BuildHierarchy(symbols);
			DisplayHierarchy(hierarchy, options);

			Console.WriteLine($"\nFound {symbols.Count} types in assembly");
			Console.WriteLine($"Total symbols: {symbols.Count + symbols.SelectMany(s => s.Children ?? new List<CodeSymbol>()).Count()}");

		} catch (Exception ex) {
			Console.WriteLine($"Error loading assembly: {ex.Message}");
			_logger.LogError(ex, "Failed to load assembly {AssemblyName}", assembly.GetName().Name);
			Environment.Exit(1);
		}

		await Task.CompletedTask;
	}

	private async Task HandleLoadedAssemblyListing(string assemblyNamePattern, LsOptions options) {
		Console.WriteLine($"Searching for loaded assemblies matching '{assemblyNamePattern}'...");

		try {
			// Get all loaded assemblies
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

			// Filter assemblies by name pattern
			List<Assembly> matchedAssemblies = assemblies
				.Where(a => a.GetName().Name != null &&
				           a.GetName().Name.Contains(assemblyNamePattern, StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (!matchedAssemblies.Any()) {
				Console.WriteLine($"No loaded assemblies found matching '{assemblyNamePattern}'");
				Console.WriteLine("\nAvailable loaded assemblies:");
				foreach (var asm in assemblies.OrderBy(a => a.GetName().Name)) {
					if (asm.GetName().Name != null)
						Console.WriteLine($"  - {asm.GetName().Name}");
				}
				return;
			}

			foreach (Assembly assembly in matchedAssemblies) {
				Console.WriteLine($"\nAssembly: {assembly.GetName().Name} v{assembly.GetName().Version}");
				Console.WriteLine(new string('=', 60));

				List<CodeSymbol> symbols = new List<CodeSymbol>();

				// Extract types from the assembly
				Type[] types;
				try {
					types = assembly.GetTypes();
				} catch (ReflectionTypeLoadException ex) {
					// Handle partial loading
					types = ex.Types.Where(t => t != null).ToArray()!;
				}

				foreach (Type type in types) {
					// Skip compiler-generated types
					if (type.Name.Contains("<") || type.Name.Contains(">"))
						continue;

					// Create a symbol for the type
					SymbolKind typeKind = SymbolKind.Class;
					if (type.IsInterface)
						typeKind = SymbolKind.Interface;

					List<CodeSymbol> typeChildren = new List<CodeSymbol>();

					// Get methods
					try {
						MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
						foreach (MethodInfo method in methods) {
							// Skip property accessors and compiler-generated methods
							if (method.Name.Contains("<") || method.Name.Contains(">") ||
							    method.Name.StartsWith("get_") || method.Name.StartsWith("set_") ||
							    method.Name.StartsWith("add_") || method.Name.StartsWith("remove_"))
								continue;

							// Build method signature
							string parameters = string.Join(", ", method.GetParameters().Select(p =>
								$"{p.ParameterType.Name} {p.Name}"));
							string methodName = $"{method.Name}({parameters})";

							CodeSymbol methodSymbol = new CodeSymbol(
								Name: methodName,
								Kind: SymbolKind.Method,
								FilePath: assembly.Location,
								StartPosition: new Position(0, 0),
								EndPosition: new Position(0, 0)
							);
							typeChildren.Add(methodSymbol);
						}
					} catch {
						// Skip types we can't reflect on
					}

					// Get properties
					try {
						PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
						foreach (PropertyInfo property in properties) {
							string propertyInfo = $"{property.Name}: {property.PropertyType.Name}";
							CodeSymbol propertySymbol = new CodeSymbol(
								Name: propertyInfo,
								Kind: SymbolKind.Property,
								FilePath: assembly.Location,
								StartPosition: new Position(0, 0),
								EndPosition: new Position(0, 0)
							);
							typeChildren.Add(propertySymbol);
						}
					} catch {
						// Skip types we can't reflect on
					}

					// Get fields
					try {
						FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
						foreach (FieldInfo field in fields) {
							// Skip compiler-generated fields
							if (field.Name.Contains("<") || field.Name.Contains(">"))
								continue;

							string fieldInfo = $"{field.Name}: {field.FieldType.Name}";
							CodeSymbol fieldSymbol = new CodeSymbol(
								Name: fieldInfo,
								Kind: SymbolKind.Field,
								FilePath: assembly.Location,
								StartPosition: new Position(0, 0),
								EndPosition: new Position(0, 0)
							);
							typeChildren.Add(fieldSymbol);
						}
					} catch {
						// Skip types we can't reflect on
					}

					// Include full type name with namespace
					string typeName = type.Namespace != null ? $"{type.Namespace}.{type.Name}" : type.Name;

					CodeSymbol typeSymbol = new CodeSymbol(
						Name: typeName,
						Kind: typeKind,
						FilePath: assembly.Location,
						StartPosition: new Position(0, 0),
						EndPosition: new Position(0, 0),
						Children: typeChildren.Any() ? typeChildren : null
					);

					symbols.Add(typeSymbol);
				}

				if (!symbols.Any()) {
					Console.WriteLine("No symbols found in assembly.");
					continue;
				}

				// Build and display hierarchy
				List<HierarchyNode> hierarchy = BuildHierarchy(symbols);
				DisplayHierarchy(hierarchy, options);

				Console.WriteLine($"\nFound {symbols.Count} types in assembly");
				Console.WriteLine($"Total symbols: {symbols.Count + symbols.SelectMany(s => s.Children ?? new List<CodeSymbol>()).Count()}");
			}

		} catch (Exception ex) {
			Console.WriteLine($"Error inspecting loaded assemblies: {ex.Message}");
			_logger.LogError(ex, "Failed to inspect loaded assembly {AssemblyName}", assemblyNamePattern);
		}

		await Task.CompletedTask;
	}

	private async Task HandleOptimizeCommand(string[] args) {
		SummarizeOptions options = ParseSummarizeOptions(args);

		Console.WriteLine($"Starting hierarchical optimization of {options.ProjectPath} ({options.Language})...");
		Console.WriteLine();

		try {
			DateTime startTime = DateTime.UtcNow;
			SymbolHierarchy hierarchy = await _summaryEngine.ProcessCodebaseAsync(options.ProjectPath, options.Language, options.CompressionLevel);
			TimeSpan duration  = DateTime.UtcNow - startTime;

			// Display extracted keys
			TraceFormatter.PrintHeader("EXTRACTED KEYS");
			foreach (KeyValuePair<string, string> key in hierarchy.ExtractedKeys) {
				TraceFormatter.PrintTrace(key.Key, key.Value.Length > 80 ? key.Value[..77] + "..." : key.Value, "KEY");
			}

			TraceFormatter.PrintHeader("OPTIMIZATION COMPLETE");
			TraceFormatter.PrintTrace("Duration", $"{duration.TotalSeconds:F2} seconds", "TIME");
			TraceFormatter.PrintTrace("Root Symbols", $"{hierarchy.RootSymbols.Count} symbols", "COUNT");
			TraceFormatter.PrintTrace("Keys Generated", $"{hierarchy.ExtractedKeys.Count} keys", "COUNT");

			Console.WriteLine();
			Console.WriteLine("Hierarchical optimization completed successfully!");
		} catch (Exception ex) {
			Console.WriteLine($"Error during optimization: {ex.Message}");
			_logger.LogError(ex, "Optimization failed");
			Environment.Exit(1);
		}
	}

	private async Task HandleLsLspCommand(string[] args) {
		bool showAll = args.Contains("--all") || args.Contains("-a");
		bool cleanup = args.Contains("--cleanup") || args.Contains("-c");

		Console.WriteLine("🔧 Thaum LSP Server Management");
		Console.WriteLine("==============================");
		Console.WriteLine();

		try {
			ILoggerFactory loggerFactory = new SerilogLoggerFactory();
			LSPManager serverManager = new LSPManager(loggerFactory.CreateLogger<LSPManager>());

			if (cleanup) {
				Console.WriteLine("🧹 Cleaning up old LSP server installations...");
				await serverManager.CleanupOldServersAsync();
				Console.WriteLine("✅ Cleanup complete!");
				return;
			}

			string cacheDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Thaum",
				"lsp-servers"
			);

			Console.WriteLine($"📁 Cache Directory: {cacheDir}");
			Console.WriteLine();

			if (!Directory.Exists(cacheDir)) {
				Console.WriteLine("No LSP servers cached yet.");
				Console.WriteLine("Run 'dotnet run -- ls <project> --lang <language>' to download servers.");
				return;
			}

			string[] languages = Directory.GetDirectories(cacheDir);
			if (!languages.Any()) {
				Console.WriteLine("No LSP servers cached yet.");
				return;
			}

			Console.WriteLine("🌐 Cached LSP Servers:");
			Console.WriteLine();

			foreach (string langDir in languages.OrderBy(Path.GetFileName)) {
				string langName    = Path.GetFileName(langDir);
				string versionFile = Path.Combine(langDir, ".version");
				string version     = "unknown";
				string installDate = "unknown";

				if (File.Exists(versionFile)) {
					version     = await File.ReadAllTextAsync(versionFile);
					installDate = File.GetCreationTime(versionFile).ToString("yyyy-MM-dd HH:mm");
				}

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write($"  📦 {langName.ToUpper()}");
				Console.ResetColor();
				Console.WriteLine($" (v{version.Trim()}) - Installed: {installDate}");

				if (showAll) {
					string[] files     = Directory.GetFiles(langDir, "*", SearchOption.AllDirectories);
					long totalSize = files.Sum(f => new FileInfo(f).Length);
					Console.WriteLine($"      Size: {totalSize / 1024 / 1024:F1} MB");
					Console.WriteLine($"      Files: {files.Length}");
					Console.WriteLine($"      Path: {langDir}");
					Console.WriteLine();
				}
			}

			if (!showAll) {
				Console.WriteLine();
				Console.WriteLine("💡 Use --all to see detailed information");
				Console.WriteLine("💡 Use --cleanup to remove old versions");
			}
		} catch (Exception ex) {
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"❌ Error: {ex.Message}");
			Console.ResetColor();
		}
	}

	private void HandleLsEnvCommand(string[] args) {
		bool showValues = args.Contains("--values") || args.Contains("-v");

		Console.WriteLine("Environment file detection and loading trace:");
		Console.WriteLine();

		EnvLoader.EnvLoadResult result = EnvLoader.LoadEnvironmentFiles();
		EnvLoader.PrintLoadTrace(result, showValues);

		Console.WriteLine();
		Console.WriteLine($"Environment variables successfully loaded and available for configuration.");
	}

	private async Task HandleLsCacheCommand(string[] args) {
		bool showKeys    = args.Contains("--keys") || args.Contains("-k");
		bool showAll     = args.Contains("--all") || args.Contains("-a");
		bool showDetails = args.Contains("--details") || args.Contains("-d");
		string pattern     = GetPatternFromArgs(args);

		// Get console width for full horizontal space usage
		int consoleWidth = Math.Max(Console.WindowWidth, 120);

		// Header with colors
		Console.ForegroundColor = ConsoleColor.Cyan;
		Console.WriteLine("🔍 Thaum Cache Browser - Hierarchical Compressed Symbol Representations");
		Console.ResetColor();
		Console.ForegroundColor = ConsoleColor.DarkCyan;
		Console.WriteLine(new string('═', consoleWidth));
		Console.ResetColor();
		Console.WriteLine();

		try {
			// Setup configuration the same way as in constructor
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: true)
				.AddEnvironmentVariables()
				.Build();

			Cache cache = new Cache(configuration, new SerilogLoggerFactory().CreateLogger<Cache>());

			// Get all cache entries with prompt metadata
			List<CacheEntryInfo> allEntries = await cache.GetAllEntriesAsync();
			List<CachedOptimization> optimizations = allEntries
				.Where(e => e.Key.StartsWith("optimization_"))
				.Where(e => string.IsNullOrEmpty(pattern) || e.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
				.Select(e => ParseOptimizationEntry(e))
				.Where(e => e != null)
				.Cast<CachedOptimization>()
				.ToList();

			if (optimizations.Any()) {
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"📦 CACHED OPTIMIZATIONS ({optimizations.Count} symbols)");
				Console.ResetColor();
				Console.WriteLine();

				// Group by file path for hierarchical organization
				List<IGrouping<string, CachedOptimization>> groupedByFile = optimizations
					.GroupBy(x => Path.GetRelativePath(Directory.GetCurrentDirectory(), x.FilePath))
					.OrderBy(g => g.Key)
					.ToList();

				foreach (IGrouping<string, CachedOptimization> fileGroup in groupedByFile) {
					// File header with color
					Console.ForegroundColor = ConsoleColor.Blue;
					Console.WriteLine($"📁 {fileGroup.Key}");
					Console.ResetColor();

					// One line per symbol with compression, prompt info, and model info
					List<CachedOptimization> fileSymbols = fileGroup.OrderBy(x => x.SymbolName).ToList();
					foreach (CachedOptimization opt in fileSymbols) {
						string icon          = GetSymbolTypeIcon(opt.SymbolName);
						string symbolDisplay = $"  {icon} ";

						Console.Write(symbolDisplay);

						// Use background coloring like ls command
						SymbolKind symbolKind = InferSymbolKind(opt.SymbolName);
						WriteColoredSymbol(opt.SymbolName, symbolKind, args.Contains("--no-colors"));

						if (opt.Line > 0) {
							Console.ForegroundColor = ConsoleColor.DarkGray;
							Console.Write($":{opt.Line}");
							Console.ResetColor();
						}

						// Show metadata in a structured way
						List<string> metadataItems = new List<string>();

						// Prompt info
						if (!string.IsNullOrEmpty(opt.PromptName)) {
							Console.ForegroundColor = ConsoleColor.Magenta;
							Console.Write($" [{opt.PromptName}]");
							Console.ResetColor();
						}

						// Model and provider info with enhanced formatting
						if (!string.IsNullOrEmpty(opt.ModelName) || !string.IsNullOrEmpty(opt.ProviderName)) {
							Console.ForegroundColor = ConsoleColor.DarkCyan;
							Console.Write(" (");
							Console.ForegroundColor = ConsoleColor.Cyan;
							Console.Write(opt.ProviderName ?? "unknown");
							Console.ForegroundColor = ConsoleColor.DarkGray;
							Console.Write(":");
							Console.ForegroundColor = ConsoleColor.Yellow;
							Console.Write(opt.ModelName ?? "unknown");
							Console.ForegroundColor = ConsoleColor.DarkCyan;
							Console.Write(")");
							Console.ResetColor();
						}

						// Show timestamp if details requested
						if (showDetails) {
							Console.ForegroundColor = ConsoleColor.DarkGray;
							Console.Write($" ⏰{opt.CreatedAt:MM-dd HH:mm}");
							if (opt.LastAccessed != opt.CreatedAt) {
								Console.Write($" (last: {opt.LastAccessed:MM-dd HH:mm})");
							}
							Console.ResetColor();
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

						Console.ForegroundColor = ConsoleColor.DarkGray;
						Console.Write(" → ");
						Console.ResetColor();
						Console.ForegroundColor = ConsoleColor.Yellow;

						// Truncate compression if too long, otherwise show full
						if (opt.Compression.Length > remainingSpace) {
							Console.WriteLine($"{opt.Compression[..(remainingSpace - 3)]}...");
						} else {
							Console.WriteLine(opt.Compression);
						}
						Console.ResetColor();
					}
					Console.WriteLine(); // Space between files
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
					Console.ForegroundColor = ConsoleColor.DarkCyan;
					Console.WriteLine(new string('═', consoleWidth));
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine("🔑 EXTRACTED ARCHITECTURAL KEYS");
					Console.ForegroundColor = ConsoleColor.DarkCyan;
					Console.WriteLine(new string('═', consoleWidth));
					Console.ResetColor();
					Console.WriteLine();

					foreach (CachedKey key in keyEntries.OrderBy(x => x.Level)) {
						Console.ForegroundColor = ConsoleColor.Green;
						Console.Write($"K{key.Level}");
						Console.ForegroundColor = ConsoleColor.DarkGray;
						Console.Write(" → ");
						Console.ResetColor();

						// Show prompt info if available
						if (!string.IsNullOrEmpty(key.PromptName)) {
							Console.ForegroundColor = ConsoleColor.Magenta;
							Console.Write($"[{key.PromptName}] ");
							Console.ResetColor();
						}

						// Show model and provider info with enhanced formatting
						if (!string.IsNullOrEmpty(key.ModelName) || !string.IsNullOrEmpty(key.ProviderName)) {
							Console.ForegroundColor = ConsoleColor.DarkCyan;
							Console.Write("(");
							Console.ForegroundColor = ConsoleColor.Cyan;
							Console.Write(key.ProviderName ?? "unknown");
							Console.ForegroundColor = ConsoleColor.DarkGray;
							Console.Write(":");
							Console.ForegroundColor = ConsoleColor.Yellow;
							Console.Write(key.ModelName ?? "unknown");
							Console.ForegroundColor = ConsoleColor.DarkCyan;
							Console.Write(") ");
							Console.ResetColor();
						}

						// Show timestamp if details requested
						if (showDetails) {
							Console.ForegroundColor = ConsoleColor.DarkGray;
							Console.Write($"⏰{key.CreatedAt:MM-dd HH:mm} ");
							if (key.LastAccessed != key.CreatedAt) {
								Console.Write($"(last: {key.LastAccessed:MM-dd HH:mm}) ");
							}
							Console.ResetColor();
						}

						Console.ForegroundColor = ConsoleColor.Green;

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
							Console.WriteLine($"{key.Pattern[..(remainingSpace - 3)]}...");
						} else {
							Console.WriteLine(key.Pattern);
						}
						Console.ResetColor();
					}
					Console.WriteLine();
				}
			}

			if (!optimizations.Any() && !showKeys) {
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine("⚠️  No cached optimizations found. Run 'summarize' first to populate cache.");
				Console.WriteLine("💡 Example: thaum summarize --compression endgame");
				Console.ResetColor();
			}
		} catch (Exception ex) {
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"❌ Error reading cache: {ex.Message}");
			Console.ResetColor();
			Environment.Exit(1);
		}
	}

	private async Task HandleTryCommand(string[] args) {
		TraceLogger.TraceEnter(parameters: new { args = string.Join(" ", args) });

		using var scope = ScopeTracer.TraceScope("HandleTryCommand");

		if (args.Length < 3) {
			TraceLogger.TraceInfo("Insufficient arguments provided");
			Console.WriteLine("Usage: thaum try <file_path> <symbol_name> [--prompt <prompt_name>] [--interactive]");
			Console.WriteLine();
			Console.WriteLine("Examples:");
			Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy");
			Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt compress_function_v2");
			Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt endgame_function");
			Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --interactive");
			TraceLogger.TraceExit();
			return;
		}

		string filePath   = args[1];
		string symbolName = args[2];

		TraceLogger.TraceInfo($"Parsed arguments: filePath='{filePath}', symbolName='{symbolName}'");

		// Parse options
		string? customPrompt = null;
		bool interactive = false;

		for (int i = 3; i < args.Length; i++) {
			switch (args[i]) {
				case "--prompt" when i + 1 < args.Length:
					customPrompt = args[++i];
					TraceLogger.TraceInfo($"Custom prompt specified: {customPrompt}");
					break;
				case "--interactive":
					interactive = true;
					TraceLogger.TraceInfo("Interactive mode enabled");
					break;
			}
		}

		// Make file path absolute
		if (!Path.IsPathRooted(filePath)) {
			string originalPath = filePath;
			filePath = Path.Combine(Directory.GetCurrentDirectory(), filePath);
			TraceLogger.TraceInfo($"Converted relative path '{originalPath}' to absolute: '{filePath}'");
		}

		if (interactive) {
			TraceLogger.TraceInfo("Initializing TraceLogger for interactive mode");
			// Re-initialize TraceLogger for interactive mode with file output
			TraceLogger.Dispose();
			TraceLogger.Initialize(_logger, isInteractiveMode: true);

			// Completely disable console logging during TUI to prevent interference
			var originalLogger = Log.Logger;
			Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Verbose()
				.WriteTo.File("output.log",
					outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
					flushToDiskInterval: TimeSpan.FromMilliseconds(500))
				.WriteTo.Seq("http://localhost:5341")
				.CreateLogger();

			// Also redirect Console.WriteLine to suppress any other console output
			var originalOut = Console.Out;
			var originalError = Console.Error;
			Console.SetOut(TextWriter.Null);
			Console.SetError(TextWriter.Null);

			try {
				TraceLogger.TraceInfo("Starting interactive mode");
				await RunInteractiveTry(filePath, symbolName, customPrompt);
			} finally {
				// Restore original logger and console output
				Log.Logger = originalLogger;
				Console.SetOut(originalOut);
				Console.SetError(originalError);
			}
		} else {
			TraceLogger.TraceInfo("Starting non-interactive mode");
			await RunNonInteractiveTry(filePath, symbolName, customPrompt);
		}

		TraceLogger.TraceExit();
	}

	private async Task RunNonInteractiveTry(string filePath, string symbolName, string? customPrompt) {
		try {
			Console.WriteLine($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}::{symbolName}");
			if (customPrompt != null) {
				Console.WriteLine($"Custom Prompt: {customPrompt}");
			} else {
				Console.WriteLine("Using default prompt from environment configuration");
			}
			Console.WriteLine();

			// Start language server
			string language = DetectLanguage(Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			bool started  = await _languageServerManager.StartLanguageServerAsync(language, Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			if (!started) {
				Console.WriteLine($"Failed to start {language} language server");
				Environment.Exit(1);
			}

			// Get symbols from file
			List<CodeSymbol> symbols      = await _languageServerManager.GetDocumentSymbolsAsync(language, filePath);
			CodeSymbol?      targetSymbol = symbols.FirstOrDefault(s => s.Name == symbolName);

			if (targetSymbol == null) {
				Console.WriteLine($"Symbol '{symbolName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}");
				Console.WriteLine();
				Console.WriteLine("Available symbols:");
				foreach (CodeSymbol sym in symbols.OrderBy(s => s.Name)) {
					Console.WriteLine($"  {sym.Name} ({sym.Kind})");
				}
				return;
			}

			Console.WriteLine($"Found symbol: {targetSymbol.Name} ({targetSymbol.Kind})");
			Console.WriteLine();

			// Get source code
			string sourceCode = await GetSymbolSourceCode(targetSymbol);
			if (string.IsNullOrEmpty(sourceCode)) {
				Console.WriteLine("Failed to extract source code for symbol");
				return;
			}

			// Determine prompt name with environment variable support
			string promptName = customPrompt ?? GetDefaultPromptFromEnvironment(targetSymbol);
			Console.WriteLine($"Using prompt: {promptName}");
			Console.WriteLine();

			// Build context (simplified for testing)
			OptimizationContext context = new OptimizationContext(
				Level: targetSymbol.Kind == SymbolKind.Function || targetSymbol.Kind == SymbolKind.Method ? 1 : 2,
				AvailableKeys: new List<string>(),          // No keys for testing
				CompressionLevel: CompressionLevel.Compress // Not used anymore, just for compatibility
			);

			// Build prompt directly
			string prompt = await BuildCustomPromptAsync(promptName, targetSymbol, context, sourceCode);

			Console.WriteLine("═══ GENERATED PROMPT ═══");
			Console.WriteLine(prompt);
			Console.WriteLine();
			Console.WriteLine("═══ TESTING LLM RESPONSE ═══");

			// Get model from configuration
			string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
						   throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

			// Setup services
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: true)
				.AddEnvironmentVariables()
				.Build();

			HttpClient httpClient    = new HttpClient();
			ILoggerFactory loggerFactory = new SerilogLoggerFactory();
			HttpLLM llmProvider   = new HttpLLM(httpClient, configuration, loggerFactory.CreateLogger<HttpLLM>());

			// Stream response
			IAsyncEnumerable<string> streamResponse = await llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));

			await foreach (string token in streamResponse) {
				Console.Write(token);
			}
			Console.WriteLine();
			Console.WriteLine();
			Console.WriteLine("═══ TEST COMPLETE ═══");
		} catch (Exception ex) {
			Console.WriteLine($"Error during prompt test: {ex.Message}");
			Environment.Exit(1);
		}
	}

	private async Task RunInteractiveTry(string filePath, string symbolName, string? customPrompt) {
		TraceLogger.TraceEnter(parameters: new { filePath, symbolName, customPrompt });
		
		using var scope = ScopeTracer.TraceScope("RunInteractiveTry");
		TraceLogger.TraceInfo("Initializing Terminal.Gui application");
		Terminal.Gui.Application.Init();
		
		// Use a semaphore to prevent multiple concurrent RefreshTryTest executions
		var refreshSemaphore = new SemaphoreSlim(1, 1);
		
		// Thread-safe shared state for UI updates
		var textLock = new object();
		string currentText = "Loading...";
		string currentStatus = "Starting...";
		
		try {
			TraceLogger.TraceInfo("Creating Terminal.Gui main view without borders");
			// Use a simple View instead of Window to avoid borders
			var mainView = new Terminal.Gui.View() {
				X = 0, Y = 0, Width = Terminal.Gui.Dim.Fill(), Height = Terminal.Gui.Dim.Fill(),
				CanFocus = true
			};

			TraceLogger.TraceInfo("Creating status bar with keyboard shortcuts");
			// Status bar with shortcuts (display only - actual bindings are global)
			var statusBar = new Terminal.Gui.StatusBar(new Terminal.Gui.StatusItem[] {
				new(Terminal.Gui.Key.Space, "~R/SPACE~ Retry", null),
				new(Terminal.Gui.Key.q, "~Q/ESC~ Quit", null),
				new(Terminal.Gui.Key.Null, "~AUTO~ Starting...", null)
			});

			TraceLogger.TraceInfo("Creating text view directly with proper colors");
			// Simplified: Just a TextView directly in the main area, no ScrollView
			var textView = new Terminal.Gui.TextView() {
				X = 0, Y = 0, 
				Width = Terminal.Gui.Dim.Fill(), 
				Height = Terminal.Gui.Dim.Fill() - 1, // Leave room for status bar
				ReadOnly = true,
				Text = "Loading...",
				WordWrap = true
			};

			// Set normal colors - white text on black background
			TraceLogger.TraceInfo("Setting proper color scheme for readability");
			textView.ColorScheme = new Terminal.Gui.ColorScheme() {
				Normal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Focus = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotNormal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotFocus = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Disabled = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.Gray, Terminal.Gui.Color.Black)
			};

			// Set main view colors too
			mainView.ColorScheme = new Terminal.Gui.ColorScheme() {
				Normal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Focus = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotNormal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotFocus = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Disabled = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.Gray, Terminal.Gui.Color.Black)
			};

			// Helper function to trigger retry
			Func<string, Task> triggerRetry = async (source) => {
				TraceLogger.TraceOperation($"Triggering retry from {source}");
				if (refreshSemaphore.CurrentCount > 0) {
					lock (textLock) {
						currentStatus = $"Auto-retry ({source})...";
					}
					_ = Task.Run(async () => {
						await refreshSemaphore.WaitAsync();
						try {
							await RefreshTryTest(textView, filePath, symbolName, customPrompt, 
								(text) => {
									TraceLogger.TraceOperation($"UI CALLBACK: Updating currentText to length {text.Length}");
									lock (textLock) {
										currentText = text;
									}
								},
								(status) => {
									TraceLogger.TraceOperation($"STATUS CALLBACK: Updating status to {status}");
									lock (textLock) {
										currentStatus = status;
									}
								});
						} finally {
							refreshSemaphore.Release();
						}
					});
				} else {
					TraceLogger.TraceOperation($"Refresh already in progress - ignoring {source} trigger");
				}
			};

			// Add global key bindings that work regardless of focus
			TraceLogger.TraceInfo("Setting up global key bindings");
			Terminal.Gui.Application.RootKeyEvent += (keyEvent) => {
				if (keyEvent.Key == Terminal.Gui.Key.q || keyEvent.Key == Terminal.Gui.Key.Q) {
					TraceLogger.TraceOperation("User pressed Q - requesting application stop");
					Terminal.Gui.Application.RequestStop();
					return true;
				} else if (keyEvent.Key == Terminal.Gui.Key.Space || keyEvent.Key == Terminal.Gui.Key.r || keyEvent.Key == Terminal.Gui.Key.R) {
					string keyPressed = keyEvent.Key == Terminal.Gui.Key.Space ? "SPACE" : 
									   keyEvent.Key == Terminal.Gui.Key.r ? "r" : "R";
					TraceLogger.TraceOperation($"User pressed {keyPressed} - attempting manual retry");
					_ = triggerRetry($"key:{keyPressed}");
					return true;
				} else if (keyEvent.Key == Terminal.Gui.Key.Esc) {
					TraceLogger.TraceOperation("User pressed ESC - requesting application stop");
					Terminal.Gui.Application.RequestStop();
					return true;
				}
				return false;
			};

			TraceLogger.TraceInfo("Adding components to Terminal.Gui layout - simplified structure");
			// Simplified structure: TextView directly in main view
			mainView.Add(textView);
			Terminal.Gui.Application.Top.Add(mainView);
			Terminal.Gui.Application.Top.Add(statusBar);

			// Setup file watcher for prompt file - RE-ENABLED
			FileSystemWatcher? promptWatcher = null;
			string? promptFilePath = null;

			TraceLogger.TraceInfo("Setting up FileSystemWatcher for prompt file auto-retry");
			if (!string.IsNullOrEmpty(customPrompt)) {
				// Determine the prompt file path
				string promptsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "prompts");
				promptFilePath = Path.Combine(promptsDirectory, $"{customPrompt}.txt");
				
				TraceLogger.TraceInfo($"Monitoring prompt file: {promptFilePath}");
				
				if (File.Exists(promptFilePath)) {
					promptWatcher = new FileSystemWatcher(Path.GetDirectoryName(promptFilePath)!, Path.GetFileName(promptFilePath)) {
						NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
						EnableRaisingEvents = true
					};

					// Debounce file changes to avoid multiple rapid triggers
					DateTime lastChangeTime = DateTime.MinValue;
					promptWatcher.Changed += (sender, e) => {
						var now = DateTime.Now;
						if (now - lastChangeTime > TimeSpan.FromMilliseconds(500)) { // 500ms debounce
							lastChangeTime = now;
							TraceLogger.TraceOperation($"Prompt file changed: {e.FullPath}");
							_ = triggerRetry("file-change");
						}
					};
					
					TraceLogger.TraceInfo("FileSystemWatcher configured and enabled for auto-retry");
				} else {
					TraceLogger.TraceInfo($"Prompt file does not exist: {promptFilePath} - auto-retry disabled");
				}
			} else {
				TraceLogger.TraceInfo("No custom prompt specified - auto-retry disabled");
			}

			// Set up timer for UI updates - this runs on the main thread
			Terminal.Gui.Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), (mainLoop) => {
				string newText, newStatus;
				lock (textLock) {
					newText = currentText;
					newStatus = currentStatus;
				}
				
				// Update text content
				if (textView.Text.ToString() != newText) {
					TraceLogger.TraceOperation($"TIMER UPDATE: Updating UI text (lengths: {textView.Text.ToString().Length} -> {newText.Length})");
					textView.Text = newText;
					textView.SetNeedsDisplay();
					textView.SetFocus();
				}
				
				// Update status bar
				string expectedStatusText = $"~AUTO~ {newStatus}";
				if (statusBar.Items[2].Title != expectedStatusText) {
					TraceLogger.TraceOperation($"TIMER UPDATE: Updating status bar from '{statusBar.Items[2].Title}' to '{expectedStatusText}'");
					statusBar.Items[2] = new Terminal.Gui.StatusItem(Terminal.Gui.Key.Null, expectedStatusText, null);
					statusBar.SetNeedsDisplay();
				}
				
				return true; // Continue the timer
			});

			// Schedule initial load after UI starts - run in background
			TraceLogger.TraceInfo("Scheduling initial content load");
			Terminal.Gui.Application.MainLoop.Invoke(() => {
				TraceLogger.TraceInfo("Starting initial RefreshTryTest");
				_ = triggerRetry("initial-load");
			});

			// Run the application
			TraceLogger.TraceInfo("Starting Terminal.Gui application main loop");
			Terminal.Gui.Application.Run();
			TraceLogger.TraceInfo("Terminal.Gui application main loop exited");

			// Cleanup
			TraceLogger.TraceInfo("Disposing FileSystemWatcher");
			promptWatcher?.Dispose();

		} finally {
			TraceLogger.TraceInfo("Shutting down Terminal.Gui application");
			refreshSemaphore?.Dispose();
			Terminal.Gui.Application.Shutdown();
			TraceLogger.TraceExit();
		}
	}

	private async Task RefreshTryTest(Terminal.Gui.TextView textView, string filePath, string symbolName, string? customPrompt, Action<string> updateCallback, Action<string> statusCallback) {
		TraceLogger.TraceEnter(parameters: new { filePath, symbolName, customPrompt });
		TraceLogger.TraceInfo("=== RefreshTryTest METHOD STARTED ===");
		
		using var scope = ScopeTracer.TraceScope("RefreshTryTest");

		try {
			// Start language server
			TraceLogger.TraceInfo("Detecting language for language server startup");
			statusCallback("Detecting language...");
			updateCallback("Detecting language...");
			
			string language = DetectLanguage(Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			TraceLogger.TraceInfo($"Detected language: {language}");
			
			TraceLogger.TraceOperation($"Starting {language} language server");
			statusCallback("Starting language server...");
			updateCallback($"Starting {language} language server...");
			
			bool started = await _languageServerManager.StartLanguageServerAsync(language, Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			
			if (!started) {
				TraceLogger.TraceInfo($"Failed to start {language} language server");
				statusCallback("Language server failed");
				updateCallback($"Failed to start {language} language server");
				TraceLogger.TraceExit();
				return;
			}
			
			TraceLogger.TraceInfo($"{language} language server started successfully");
			statusCallback("Loading symbols...");
			updateCallback("Loading symbols...");

			// Get symbols from file with timeout
			TraceLogger.TraceInfo($"Parsing symbols from file: {filePath}");
			List<CodeSymbol> symbols;
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			try {
				symbols = await _languageServerManager.GetDocumentSymbolsAsync(language, filePath).WaitAsync(cts.Token);
				TraceLogger.TraceInfo($"Successfully parsed {symbols.Count} symbols from file");
			} catch (OperationCanceledException) {
				TraceLogger.TraceInfo("Symbol parsing timed out");
				statusCallback("Symbol parsing timeout");
				updateCallback("Symbol parsing timed out. The file might be too large or contain problematic syntax.");
				TraceLogger.TraceExit();
				return;
			}
			
			TraceLogger.TraceInfo($"Searching for target symbol: {symbolName}");
			CodeSymbol? targetSymbol = symbols.FirstOrDefault(s => s.Name == symbolName);

			if (targetSymbol == null) {
				TraceLogger.TraceInfo($"Target symbol '{symbolName}' not found. Available symbols: {symbols.Count}");
				var availableSymbols = string.Join("\n", symbols.OrderBy(s => s.Name).Select(s => $"  {s.Name} ({s.Kind})"));
				statusCallback("Symbol not found");
				updateCallback($"Symbol '{symbolName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}\n\nAvailable symbols:\n{availableSymbols}");
				TraceLogger.TraceExit();
				return;
			}
			
			TraceLogger.TraceInfo($"Found target symbol: {symbolName} (Kind: {targetSymbol.Kind})");
			statusCallback("Extracting source...");
			updateCallback("Extracting source code...");

			// Get source code
			TraceLogger.TraceOperation("Extracting source code from target symbol");
			string sourceCode = await GetSymbolSourceCode(targetSymbol);
			if (string.IsNullOrEmpty(sourceCode)) {
				TraceLogger.TraceInfo("Failed to extract source code for symbol");
				statusCallback("Source extraction failed");
				updateCallback("Failed to extract source code for symbol");
				TraceLogger.TraceExit();
				return;
			}
			TraceLogger.TraceInfo($"Source code extracted successfully (length: {sourceCode.Length} chars)");

			// Determine prompt name
			string promptName = customPrompt ?? GetDefaultPromptFromEnvironment(targetSymbol);
			TraceLogger.TraceInfo($"Using prompt: {promptName}");
			
			// Build context
			OptimizationContext context = new OptimizationContext(
				Level: targetSymbol.Kind == SymbolKind.Function || targetSymbol.Kind == SymbolKind.Method ? 1 : 2,
				AvailableKeys: new List<string>(),
				CompressionLevel: CompressionLevel.Compress
			);

			statusCallback("Building prompt...");
			updateCallback("Building prompt...");

			// Build prompt
			TraceLogger.TraceOperation("Building custom prompt");
			string prompt;
			try {
				prompt = await BuildCustomPromptAsync(promptName, targetSymbol, context, sourceCode);
				TraceLogger.TraceInfo($"Custom prompt built successfully (length: {prompt.Length} chars)");
			} catch (Exception promptEx) {
				TraceLogger.TraceInfo($"Failed to build custom prompt: {promptEx.Message}");
				statusCallback("Prompt build failed");
				updateCallback($"Failed to build custom prompt: {promptEx.Message}\n\nStack trace:\n{promptEx.StackTrace}");
				TraceLogger.TraceExit();
				return;
			}

			var output = new System.Text.StringBuilder();
			
			output.AppendLine($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}::{symbolName}");
			output.AppendLine($"Using prompt: {promptName}");
			output.AppendLine();
			output.AppendLine("═══ GENERATED PROMPT ═══");
			output.AppendLine(prompt);
			output.AppendLine();
			output.AppendLine("═══ LLM RESPONSE ═══");

			// Get model from configuration
			string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ?? 
						   throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

			TraceLogger.TraceInfo($"Using model: {model}");

			// Setup services
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: true)
				.AddEnvironmentVariables()
				.Build();

			HttpClient httpClient = new HttpClient();
			ILoggerFactory loggerFactory = new SerilogLoggerFactory();
			HttpLLM llmProvider = new HttpLLM(httpClient, configuration, loggerFactory.CreateLogger<HttpLLM>());

			// Update text view with current content
			TraceLogger.TraceOperation("Updating UI with prompt content before streaming");
			statusCallback("Connecting to LLM...");
			updateCallback(output.ToString());

			// Stream response
			TraceLogger.TraceOperation($"Starting LLM streaming request with model: {model}");
			IAsyncEnumerable<string> streamResponse;
			try {
				streamResponse = await llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));
				TraceLogger.TraceInfo("LLM streaming request initiated successfully");
				statusCallback("Streaming response...");
			} catch (Exception llmEx) {
				TraceLogger.TraceInfo($"Failed to start LLM streaming: {llmEx.Message}");
				statusCallback("LLM connection failed");
				updateCallback($"Failed to start LLM streaming: {llmEx.Message}\n\nStack trace:\n{llmEx.StackTrace}");
				TraceLogger.TraceExit();
				return;
			}

			TraceLogger.TraceOperation("Starting to consume LLM stream");
			int tokenCount = 0;
			await foreach (string token in streamResponse) {
				tokenCount++;
				output.Append(token);
				
				// Update UI every 10 tokens for more responsive display
				if (tokenCount % 10 == 0) {
					TraceLogger.TraceOperation($"Updating UI at token {tokenCount}");
					updateCallback(output.ToString());
				}
				
				if (tokenCount % 50 == 0) {
					TraceLogger.TraceInfo($"Streaming progress: {tokenCount} tokens received");
				}
			}
			
			TraceLogger.TraceInfo($"LLM streaming completed. Total tokens received: {tokenCount}");

			output.AppendLine();
			output.AppendLine();
			output.AppendLine("═══ TEST COMPLETE ═══");
			
			TraceLogger.TraceInfo("Updating final UI with complete results");
			statusCallback("Complete - Ready for retry");
			updateCallback(output.ToString());
			TraceLogger.TraceOperation("RefreshTryTest completed successfully");

		} catch (Exception ex) {
			TraceLogger.TraceInfo($"Exception occurred during prompt test: {ex.Message}");
			TraceLogger.TraceInfo($"Exception stack trace: {ex.StackTrace}");
			statusCallback("Error occurred");
			updateCallback($"Error during prompt test: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
		}
		
		TraceLogger.TraceExit();
	}

	private async Task RefreshTryTestSync(Terminal.Gui.TextView textView, string filePath, string symbolName, string? customPrompt) {
		TraceLogger.TraceEnter(parameters: new { filePath, symbolName, customPrompt });
		TraceLogger.TraceInfo("=== RefreshTryTestSync METHOD STARTED ===");
		
		using var scope = ScopeTracer.TraceScope("RefreshTryTestSync");

		// Helper method to update UI directly on main thread
		void UpdateUI(string text) {
			TraceLogger.TraceOperation($"UI UPDATE: Setting textView.Text to text of length {text.Length}");
			TraceLogger.TraceInfo($"UI UPDATE: First 100 chars: {text.Substring(0, Math.Min(100, text.Length))}");
			
			textView.Text = text;
			TraceLogger.TraceOperation("UI UPDATE: Called textView.SetNeedsDisplay()");
			textView.SetNeedsDisplay();
			TraceLogger.TraceOperation("UI UPDATE: Called Terminal.Gui.Application.Refresh()");
			Terminal.Gui.Application.Refresh();
			TraceLogger.TraceOperation("UI UPDATE: UI update sequence completed");
		}

		try {
			// Start language server
			TraceLogger.TraceInfo("Detecting language for language server startup");
			UpdateUI("Detecting language...");
			
			string language = DetectLanguage(Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			TraceLogger.TraceInfo($"Detected language: {language}");
			
			TraceLogger.TraceOperation($"Starting {language} language server");
			UpdateUI($"Starting {language} language server...");
			
			bool started = await _languageServerManager.StartLanguageServerAsync(language, Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			
			if (!started) {
				TraceLogger.TraceInfo($"Failed to start {language} language server");
				UpdateUI($"Failed to start {language} language server");
				TraceLogger.TraceExit();
				return;
			}
			
			TraceLogger.TraceInfo($"{language} language server started successfully");
			UpdateUI("Loading symbols...");

			// Get symbols from file with timeout
			TraceLogger.TraceInfo($"Parsing symbols from file: {filePath}");
			List<CodeSymbol> symbols;
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			try {
				symbols = await _languageServerManager.GetDocumentSymbolsAsync(language, filePath).WaitAsync(cts.Token);
				TraceLogger.TraceInfo($"Successfully parsed {symbols.Count} symbols from file");
			} catch (OperationCanceledException) {
				TraceLogger.TraceInfo("Symbol parsing timed out");
				UpdateUI("Symbol parsing timed out. The file might be too large or contain problematic syntax.");
				TraceLogger.TraceExit();
				return;
			}
			
			TraceLogger.TraceInfo($"Searching for target symbol: {symbolName}");
			CodeSymbol? targetSymbol = symbols.FirstOrDefault(s => s.Name == symbolName);

			if (targetSymbol == null) {
				TraceLogger.TraceInfo($"Target symbol '{symbolName}' not found. Available symbols: {symbols.Count}");
				var availableSymbols = string.Join("\n", symbols.OrderBy(s => s.Name).Select(s => $"  {s.Name} ({s.Kind})"));
				UpdateUI($"Symbol '{symbolName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}\n\nAvailable symbols:\n{availableSymbols}");
				TraceLogger.TraceExit();
				return;
			}
			
			TraceLogger.TraceInfo($"Found target symbol: {symbolName} (Kind: {targetSymbol.Kind})");
			UpdateUI("Extracting source code...");

			// Get source code
			TraceLogger.TraceOperation("Extracting source code from target symbol");
			string sourceCode = await GetSymbolSourceCode(targetSymbol);
			if (string.IsNullOrEmpty(sourceCode)) {
				TraceLogger.TraceInfo("Failed to extract source code for symbol");
				UpdateUI("Failed to extract source code for symbol");
				TraceLogger.TraceExit();
				return;
			}
			TraceLogger.TraceInfo($"Source code extracted successfully (length: {sourceCode.Length} chars)");

			// Determine prompt name
			string promptName = customPrompt ?? GetDefaultPromptFromEnvironment(targetSymbol);
			TraceLogger.TraceInfo($"Using prompt: {promptName}");
			
			// Build context
			OptimizationContext context = new OptimizationContext(
				Level: targetSymbol.Kind == SymbolKind.Function || targetSymbol.Kind == SymbolKind.Method ? 1 : 2,
				AvailableKeys: new List<string>(),
				CompressionLevel: CompressionLevel.Compress
			);

			UpdateUI("Building prompt...");

			// Build prompt
			TraceLogger.TraceOperation("Building custom prompt");
			string prompt;
			try {
				prompt = await BuildCustomPromptAsync(promptName, targetSymbol, context, sourceCode);
				TraceLogger.TraceInfo($"Custom prompt built successfully (length: {prompt.Length} chars)");
			} catch (Exception promptEx) {
				TraceLogger.TraceInfo($"Failed to build custom prompt: {promptEx.Message}");
				UpdateUI($"Failed to build custom prompt: {promptEx.Message}\n\nStack trace:\n{promptEx.StackTrace}");
				TraceLogger.TraceExit();
				return;
			}

			var output = new System.Text.StringBuilder();
			
			output.AppendLine($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}::{symbolName}");
			output.AppendLine($"Using prompt: {promptName}");
			output.AppendLine();
			output.AppendLine("═══ GENERATED PROMPT ═══");
			output.AppendLine(prompt);
			output.AppendLine();
			output.AppendLine("═══ LLM RESPONSE ═══");

			// Get model from configuration
			string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ?? 
						   throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

			TraceLogger.TraceInfo($"Using model: {model}");

			// Setup services
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: true)
				.AddEnvironmentVariables()
				.Build();

			HttpClient httpClient = new HttpClient();
			ILoggerFactory loggerFactory = new SerilogLoggerFactory();
			HttpLLM llmProvider = new HttpLLM(httpClient, configuration, loggerFactory.CreateLogger<HttpLLM>());

			// Update text view with current content
			TraceLogger.TraceOperation("Updating UI with prompt content before streaming");
			UpdateUI(output.ToString());

			// Stream response
			TraceLogger.TraceOperation($"Starting LLM streaming request with model: {model}");
			IAsyncEnumerable<string> streamResponse;
			try {
				streamResponse = await llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));
				TraceLogger.TraceInfo("LLM streaming request initiated successfully");
			} catch (Exception llmEx) {
				TraceLogger.TraceInfo($"Failed to start LLM streaming: {llmEx.Message}");
				UpdateUI($"Failed to start LLM streaming: {llmEx.Message}\n\nStack trace:\n{llmEx.StackTrace}");
				TraceLogger.TraceExit();
				return;
			}

			TraceLogger.TraceOperation("Starting to consume LLM stream");
			int tokenCount = 0;
			await foreach (string token in streamResponse) {
				tokenCount++;
				output.Append(token);
				
				// Update UI every 5 tokens for more responsive display
				if (tokenCount % 5 == 0) {
					TraceLogger.TraceOperation($"Updating UI at token {tokenCount}");
					UpdateUI(output.ToString());
				}
				
				if (tokenCount % 50 == 0) {
					TraceLogger.TraceInfo($"Streaming progress: {tokenCount} tokens received");
				}
			}
			
			TraceLogger.TraceInfo($"LLM streaming completed. Total tokens received: {tokenCount}");

			output.AppendLine();
			output.AppendLine();
			output.AppendLine("═══ TEST COMPLETE ═══");
			
			TraceLogger.TraceInfo("Updating final UI with complete results");
			UpdateUI(output.ToString());
			TraceLogger.TraceOperation("RefreshTryTestSync completed successfully");

		} catch (Exception ex) {
			TraceLogger.TraceInfo($"Exception occurred during prompt test: {ex.Message}");
			TraceLogger.TraceInfo($"Exception stack trace: {ex.StackTrace}");
			UpdateUI($"Error during prompt test: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
		}
		
		TraceLogger.TraceExit();
	}


	private async Task<string> GetSymbolSourceCode(CodeSymbol symbol) {
		try {
			string[] lines     = await File.ReadAllLinesAsync(symbol.FilePath);
			int startLine = Math.Max(0, symbol.StartPosition.Line);
			int endLine   = Math.Min(lines.Length - 1, symbol.EndPosition.Line);

			// Debug output
			Console.WriteLine($"Debug: StartLine={startLine}, EndLine={endLine}, TotalLines={lines.Length}");
			Console.WriteLine($"Debug: StartLine content: '{lines[startLine]}'");
			Console.WriteLine($"Debug: EndLine content: '{lines[endLine]}'");

			return string.Join("\n", lines[startLine..(endLine + 1)]);
		} catch (Exception) {
			return "";
		}
	}

	private async Task<string> BuildCustomPromptAsync(string promptName, CodeSymbol symbol, OptimizationContext context, string sourceCode) {
		Dictionary<string, object> parameters = new Dictionary<string, object> {
			["sourceCode"] = sourceCode,
			["symbolName"] = symbol.Name,
			["availableKeys"] = context.AvailableKeys.Any()
				? string.Join("\n", context.AvailableKeys.Select(k => $"- {k}"))
				: "None"
		};

		ILoggerFactory loggerFactory = new SerilogLoggerFactory();
		PromptLoader promptLoader  = new PromptLoader(loggerFactory.CreateLogger<PromptLoader>());

		return await promptLoader.FormatPromptAsync(promptName, parameters);
	}

	private string GetDefaultPromptFromEnvironment(CodeSymbol symbol) {
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
		return symbolType == "function" ? "compress_function_v2" : "compress_class";
	}

	private async Task<List<CachedOptimization>> GetCachedOptimizations(Cache cache, string? pattern) {
		List<CachedOptimization> results = new List<CachedOptimization>();

		// This is a bit hacky since SqliteCacheService doesn't expose a query method
		// We'll need to add this functionality
		string cacheDbPath = Path.Combine("cache", "cache.db");
		if (!File.Exists(cacheDbPath)) return results;

		using SqliteConnection connection = new SqliteConnection($"Data Source={cacheDbPath}");
		await connection.OpenAsync();

		string query = @"
            SELECT key, value 
            FROM cache_entries 
            WHERE key LIKE 'optimization_%' 
            ORDER BY key";

		if (!string.IsNullOrEmpty(pattern)) {
			query += $" AND (key LIKE '%{pattern}%' OR value LIKE '%{pattern}%')";
		}

		using SqliteCommand command = new SqliteCommand(query, connection);
		using SqliteDataReader reader  = await command.ExecuteReaderAsync();

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

	private async Task<List<CachedKey>> GetCachedKeys(Cache cache) {
		List<CachedKey> results = new List<CachedKey>();

		string cacheDbPath = Path.Combine("cache", "cache.db");
		if (!File.Exists(cacheDbPath)) return results;

		using SqliteConnection connection = new SqliteConnection($"Data Source={cacheDbPath}");
		await connection.OpenAsync();

		string query = @"
            SELECT key, value 
            FROM cache_entries 
            WHERE key LIKE 'key_L%' 
            ORDER BY key";

		using SqliteCommand command = new SqliteCommand(query, connection);
		using SqliteDataReader reader  = await command.ExecuteReaderAsync();

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

	private string GetPatternFromArgs(string[] args) {
		for (int i = 1; i < args.Length; i++) {
			if (!args[i].StartsWith("-") && i > 0 && !args[i - 1].StartsWith("-")) {
				return args[i];
			}
		}
		return string.Empty;
	}

	private string GetSymbolTypeIcon(string symbolName) {
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

	private string WrapText(string text, int maxWidth, string indent) {
		if (text.Length <= maxWidth) return text;

		string[]     words       = text.Split(' ');
		List<string> lines       = new List<string>();
		string          currentLine = "";

		foreach (string word in words) {
			if (currentLine.Length + word.Length + 1 <= maxWidth) {
				currentLine += (currentLine.Length > 0 ? " " : "") + word;
			} else {
				if (currentLine.Length > 0) lines.Add(currentLine);
				currentLine = word;
			}
		}

		if (currentLine.Length > 0) lines.Add(currentLine);
		return string.Join("\n" + indent, lines);
	}

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


	private static CachedOptimization? ParseOptimizationEntry(CacheEntryInfo entry) {
		try {
			// Parse key format: optimization_{symbolName}_{filePath}_{line}_{level}
			string[] keyParts = entry.Key.Split('_', 3);
			if (keyParts.Length < 3) return null;

			string remainingPart       = keyParts[2]; // Everything after "optimization_"
			int lastUnderscoreIndex = remainingPart.LastIndexOf('_');
			if (lastUnderscoreIndex == -1) return null;

			int secondLastUnderscoreIndex = remainingPart.LastIndexOf('_', lastUnderscoreIndex - 1);
			if (secondLastUnderscoreIndex == -1) return null;

			string symbolName = remainingPart[..secondLastUnderscoreIndex];
			string filePath   = remainingPart[(secondLastUnderscoreIndex + 1)..lastUnderscoreIndex];
			string lineStr    = remainingPart[(lastUnderscoreIndex + 1)..];

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
		int maxDepth    = 10;
		bool showTypes   = false;
		bool noColors    = false;

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
		string projectPath      = Directory.GetCurrentDirectory();
		string language         = "auto";
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

		Dictionary<string, int> counts = extensions.GroupBy(ext => ext.ToLowerInvariant())
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
			if (counts.GetValueOrDefault(".ts", 0) > counts.GetValueOrDefault(".js", 0))
				return "typescript";
			return "javascript";
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

	private List<HierarchyNode> BuildHierarchy(List<CodeSymbol> symbols) {
		List<HierarchyNode>                        nodes         = new List<HierarchyNode>();
		IEnumerable<IGrouping<string, CodeSymbol>> symbolsByFile = symbols.GroupBy(s => s.FilePath);

		foreach (IGrouping<string, CodeSymbol> fileGroup in symbolsByFile) {
			string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fileGroup.Key);
			HierarchyNode fileNode     = new HierarchyNode(relativePath, SymbolKind.Module, null);

			// Include all meaningful symbol types (for both code and assembly inspection)
			List<CodeSymbol> filteredSymbols = fileGroup.Where(s =>
				s.Kind == SymbolKind.Class ||
				s.Kind == SymbolKind.Interface ||
				s.Kind == SymbolKind.Enum ||
				s.Kind == SymbolKind.EnumMember ||
				s.Kind == SymbolKind.Namespace ||
				s.Kind == SymbolKind.Function ||
				s.Kind == SymbolKind.Method ||
				s.Kind == SymbolKind.Constructor ||
				s.Kind == SymbolKind.Property ||
				s.Kind == SymbolKind.Field).ToList();

			if (filteredSymbols.Any()) {
				foreach (CodeSymbol symbol in filteredSymbols.OrderBy(s => s.StartPosition.Line)) {
					HierarchyNode symbolNode = new HierarchyNode(symbol.Name, symbol.Kind, symbol);
					fileNode.Children.Add(symbolNode);

					// Add nested symbols if any (only classes and functions)
					if (symbol.Children?.Any() == true) {
						AddChildSymbols(symbolNode, symbol.Children);
					}
				}

				nodes.Add(fileNode);
			}
		}

		return nodes.OrderBy(n => n.Name).ToList();
	}

	private void AddChildSymbols(HierarchyNode parent, List<CodeSymbol> children) {
		// Include all meaningful symbol types for assembly inspection
		List<CodeSymbol> filteredChildren = children.Where(c =>
			c.Kind == SymbolKind.Class ||
			c.Kind == SymbolKind.Interface ||
			c.Kind == SymbolKind.Function ||
			c.Kind == SymbolKind.Method ||
			c.Kind == SymbolKind.Property ||
			c.Kind == SymbolKind.Field).ToList();

		foreach (CodeSymbol child in filteredChildren.OrderBy(c => c.StartPosition.Line)) {
			HierarchyNode childNode = new HierarchyNode(child.Name, child.Kind, child);
			parent.Children.Add(childNode);

			if (child.Children?.Any() == true) {
				AddChildSymbols(childNode, child.Children);
			}
		}
	}

	private void DisplayHierarchy(List<HierarchyNode> nodes, LsOptions options) {
		foreach (HierarchyNode node in nodes) {
			DisplayNodeGrouped(node, "", true, options, 0);
		}
	}

	private void DisplayNodeGrouped(HierarchyNode node, string prefix, bool isLast, LsOptions options, int depth) {
		if (depth >= options.MaxDepth) return;

		string connector = isLast ? "└── " : "├── ";
		string symbol    = GetSymbolIcon(node.Kind);

		Console.WriteLine($"{prefix}{connector}{symbol} {node.Name}");

		if (node.Children.Any()) {
			string newPrefix = prefix + (isLast ? "    " : "│   ");

			// Group children by type
			List<IGrouping<SymbolKind, HierarchyNode>> groupedChildren = node.Children.GroupBy(c => c.Kind).ToList();

			foreach (IGrouping<SymbolKind, HierarchyNode> group in groupedChildren) {
				bool isLastGroup = group == groupedChildren.Last();
				DisplaySymbolGroup(group.Key, group.ToList(), newPrefix, isLastGroup, options, depth + 1);
			}
		}
	}

	private void DisplaySymbolGroup(SymbolKind kind, List<HierarchyNode> symbols, string prefix, bool isLast, LsOptions options, int depth) {
		if (depth >= options.MaxDepth || !symbols.Any()) return;

		string connector = isLast ? "└── " : "├── ";
		string icon      = GetSymbolIcon(kind);
		string kindName  = GetKindDisplayName(kind);

		// Print prefix and label normally
		string linePrefix = $"{prefix}{connector}{icon} {kindName}: ";
		Console.Write(linePrefix);

		// Print symbols with Terminal.Gui colors
		PrintColoredSymbols(symbols, kind, 80 - linePrefix.Length, options.NoColors);

		Console.WriteLine(); // End the line
	}

	private void PrintColoredSymbols(List<HierarchyNode> symbols, SymbolKind kind, int maxWidth, bool noColors = false) {
		int currentWidth  = 0;
		bool isFirstSymbol = true;

		foreach (HierarchyNode symbol in symbols) {
			bool needsSpace  = !isFirstSymbol;
			int symbolWidth = symbol.Name.Length + (needsSpace ? 1 : 0);

			// Check if we need to wrap
			if (currentWidth + symbolWidth > maxWidth && currentWidth > 0) {
				Console.WriteLine();
				Console.Write(new string(' ', 80 - maxWidth)); // Indent continuation
				currentWidth = 0;
				needsSpace   = false;
			}

			if (needsSpace) {
				Console.Write(" ");
				currentWidth += 1;
			}

			// Use proper color helper method
			WriteColoredSymbol(symbol.Name, kind, noColors);

			currentWidth  += symbol.Name.Length;
			isFirstSymbol =  false;
		}
	}

	private void WriteColoredSymbol(string symbolName, SymbolKind kind, bool noColors = false) {
		if (noColors) {
			Console.Write(symbolName);
			return;
		}

		SemanticColorType semanticType = kind switch {
			SymbolKind.Function  => SemanticColorType.Function,
			SymbolKind.Method    => SemanticColorType.Function,
			SymbolKind.Class     => SemanticColorType.Class,
			SymbolKind.Interface => SemanticColorType.Interface,
			SymbolKind.Module    => SemanticColorType.Module,
			SymbolKind.Namespace => SemanticColorType.Namespace,
			_                    => SemanticColorType.Function
		};

		(int r, int g, int b) = _colorEngine.GenerateSemanticColor(symbolName, semanticType);
		Console.Write($"\u001b[48;2;{r};{g};{b}m\u001b[38;2;0;0;0m{symbolName}\u001b[0m");
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


	private string GetKindDisplayName(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "functions",
			SymbolKind.Method    => "methods",
			SymbolKind.Class     => "classes",
			SymbolKind.Interface => "interfaces",
			SymbolKind.Module    => "modules",
			SymbolKind.Namespace => "namespaces",
			_                    => kind.ToString().ToLower()
		};
	}

	private string GetBackgroundColor(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "\u001b[48;2;70;130;180m",  // Steel Blue
			SymbolKind.Method    => "\u001b[48;2;100;149;237m", // Cornflower Blue
			SymbolKind.Class     => "\u001b[48;2;135;206;235m", // Sky Blue
			SymbolKind.Interface => "\u001b[48;2;173;216;230m", // Light Blue
			SymbolKind.Module    => "\u001b[48;2;176;196;222m", // Light Steel Blue
			SymbolKind.Namespace => "\u001b[48;2;191;239;255m", // Alice Blue
			_                    => "\u001b[48;2;220;220;220m"  // Light Gray
		};
	}

	private string GetSymbolIcon(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "ƒ",
			SymbolKind.Method    => "ƒ",
			SymbolKind.Class     => "C",
			SymbolKind.Interface => "I",
			SymbolKind.Module    => "📁",
			SymbolKind.Namespace => "N",
			SymbolKind.Property  => "P",
			SymbolKind.Field     => "F",
			SymbolKind.Variable  => "V",
			SymbolKind.Parameter => "p",
			_                    => "?"
		};
	}

	private void ShowHelp() {
		Console.WriteLine("Thaum - Hierarchical Compression Engine");
		Console.WriteLine();
		Console.WriteLine("Usage:");
		Console.WriteLine("  thaum <command> [options]");
		Console.WriteLine();
		Console.WriteLine("Commands:");
		Console.WriteLine("  ls [path]              List symbols in hierarchical format");
		Console.WriteLine("  ls assembly:<name>     List symbols in loaded assembly (e.g., 'ls assembly:TreeSitter')");
		Console.WriteLine("  ls-env [--values]      Show .env file detection and merging trace");
		Console.WriteLine("  ls-cache [pattern]     Browse cached symbol compressions");
		Console.WriteLine("  ls-lsp [--all] [--cleanup]  Manage auto-downloaded LSP servers");
		Console.WriteLine("  try <file> <symbol>    Test prompts on individual symbols");
		Console.WriteLine("  optimize [path]        Generate codebase optimizations");
		Console.WriteLine("  help                   Show this help message");
		Console.WriteLine();
		Console.WriteLine("Options for 'ls':");
		Console.WriteLine("  --path <path>          Project path (default: current directory)");
		Console.WriteLine("  --lang <language>      Language (python, csharp, javascript, etc.)");
		Console.WriteLine("  --depth <number>       Maximum nesting depth (default: 10)");
		Console.WriteLine("  --types                Show symbol types");
		Console.WriteLine("  --no-colors            Disable colored output");
		Console.WriteLine();
		Console.WriteLine("Options for 'ls-env':");
		Console.WriteLine("  --values, -v           Show actual environment variable values");
		Console.WriteLine();
		Console.WriteLine("Options for 'ls-cache':");
		Console.WriteLine("  [pattern]              Filter cached symbols by pattern");
		Console.WriteLine("  --keys, -k             Show K1/K2 architectural keys");
		Console.WriteLine("  --all, -a              Show both optimizations and keys");
		Console.WriteLine();
		Console.WriteLine("Options for 'ls-lsp':");
		Console.WriteLine("  --all, -a              Show detailed information about cached servers");
		Console.WriteLine("  --cleanup, -c          Remove old LSP server versions");
		Console.WriteLine();
		Console.WriteLine("Options for 'try':");
		Console.WriteLine("  <file_path>            Path to source file");
		Console.WriteLine("  <symbol_name>          Name of symbol to test");
		Console.WriteLine("  --prompt <name>        Prompt file name (e.g., compress_function_v2, endgame_function)");
		Console.WriteLine("  --interactive          Launch interactive TUI with live updates");
		Console.WriteLine();
		Console.WriteLine("Options for 'optimize':");
		Console.WriteLine("  --path <path>          Project path (default: current directory)");
		Console.WriteLine("  --lang <language>      Language (python, csharp, javascript, etc.)");
		Console.WriteLine("  --compression <level>  Compression level: optimize, compress, golf, endgame");
		Console.WriteLine("  -c <level>             Short form of --compression");
		Console.WriteLine("  --endgame              Use maximum endgame compression");
		Console.WriteLine();
		Console.WriteLine("Environment Variables:");
		Console.WriteLine("  THAUM_DEFAULT_FUNCTION_PROMPT  Default prompt for functions (default: compress_function_v2)");
		Console.WriteLine("  THAUM_DEFAULT_CLASS_PROMPT     Default prompt for classes (default: compress_class)");
		Console.WriteLine("  LLM__DefaultModel              LLM model to use for compression");
		Console.WriteLine();
		Console.WriteLine("Available Prompts:");
		Console.WriteLine("  optimize_function, optimize_class, optimize_key");
		Console.WriteLine("  compress_function, compress_function_v2, compress_class, compress_key");
		Console.WriteLine("  golf_function, golf_class, golf_key");
		Console.WriteLine("  endgame_function, endgame_class, endgame_key");
		Console.WriteLine();
		Console.WriteLine("Examples:");
		Console.WriteLine("  thaum ls");
		Console.WriteLine("  thaum ls /path/to/project --lang python --depth 3");
		Console.WriteLine("  thaum ls-cache");
		Console.WriteLine("  thaum ls-cache Handle --keys");
		Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy");
		Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt endgame_function");
		Console.WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --interactive");
		Console.WriteLine("  thaum optimize --compression endgame");
		Console.WriteLine("  thaum optimize /path/to/project -c golf");
		Console.WriteLine("  thaum ls-env --values");
		Console.WriteLine();
		Console.WriteLine("Environment Variable Examples:");
		Console.WriteLine("  export THAUM_DEFAULT_FUNCTION_PROMPT=endgame_function");
		Console.WriteLine("  export THAUM_DEFAULT_CLASS_PROMPT=golf_class");
		Console.WriteLine();
		Console.WriteLine("Run without arguments to launch the interactive TUI.");
	}
}

internal record LsOptions(string ProjectPath, string Language, int MaxDepth, bool ShowTypes, bool NoColors = false);

internal record SummarizeOptions(string ProjectPath, string Language, CompressionLevel CompressionLevel = CompressionLevel.Optimize);

internal class HierarchyNode {
	public string              Name     { get; }
	public SymbolKind          Kind     { get; }
	public CodeSymbol?         Symbol   { get; }
	public List<HierarchyNode> Children { get; } = new();

	public HierarchyNode(string name, SymbolKind kind, CodeSymbol? symbol) {
		Name   = name;
		Kind   = kind;
		Symbol = symbol;
	}
}