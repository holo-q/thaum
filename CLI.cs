using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Reflection;
using System.CommandLine;
using Thaum.CLI.Models;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using static System.Console;
using static Thaum.Core.Utils.Tracer;
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
	private readonly Crawler           _crawler;
	private readonly Prompter          _prompter;
	private readonly Compressor        _compressor;

	/// <summary>
	/// Initializes CLI with ambient services where HttpLLM provides AI capabilities where
	/// TreeSitterCrawler enables code analysis where PerceptualColorer creates visual semantics
	/// where EnvLoader enables hierarchical config where trace logging captures execution flow
	/// </summary>
	public CLI() {
		HttpClient httpClient = new HttpClient();

		_crawler  = new TreeSitterCrawler();
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
	/// Main entry point using System.CommandLine for robust argument parsing where commands
	/// are defined declaratively with automatic help generation where type safety prevents
	/// errors where validation happens before handlers execute eliminating manual parsing
	/// </summary>
	public async Task RunAsync(string[] args) {
		tracein(parameters: new { args = string.Join(" ", args) });

		using var scope = trace_scope("RunAsync");

		// Create root command with all subcommands
		var rootCommand = CLI_Commands.CreateRootCommand(this);

		// If no arguments, show help
		if (args.Length == 0) {
			trace("No arguments provided, showing help");
			args = ["--help"];
		}

		trace($"Processing command with System.CommandLine: {string.Join(" ", args)}");

		// Execute command through System.CommandLine
		int result = await rootCommand.Parse(args).InvokeAsync();

		trace($"Command completed with exit code: {result}");
		traceout();
	}

	// Internal async methods for System.CommandLine handlers
	public async Task CMD_ls(LsOptions options) {
		trace($"Executing ls command with options: {options}");

		// Handle assembly specifiers
		if (options.ProjectPath.StartsWith("assembly:")) {
			string assemblyName = options.ProjectPath[9..]; // Remove "assembly:" prefix
			await HandleAssemblyCommand(assemblyName, options);
			return;
		}

		// Handle file paths for assemblies
		if (options.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		    options.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
			if (!File.Exists(options.ProjectPath)) {
				println($"Error: Could not find assembly file: {options.ProjectPath}");
				println($"Current directory: {Directory.GetCurrentDirectory()}");
				return;
			}

			Assembly fileAssembly = Assembly.LoadFrom(options.ProjectPath);
			await CMD_ls_assembly(fileAssembly, options);
			return;
		}

		// If we reach here and the path doesn't exist, list available assemblies to help debug
		if (!Directory.Exists(options.ProjectPath) && !File.Exists(options.ProjectPath)) {
			println($"Path '{options.ProjectPath}' not found.");
			println("\nAvailable loaded assemblies:");
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				println($"  - {name}");
			}
			println("\nTry 'thaum ls assembly:<assembly-name>' where <assembly-name> is one of the above.");
			return;
		}

		println($"Scanning {options.ProjectPath} for {options.Language} symbols...");

		// Get symbols
		List<CodeSymbol> symbols = await _crawler.CrawlDir(options.ProjectPath);

		if (!symbols.Any()) {
			println("No symbols found.");
			return;
		}

		// Build and display hierarchy
		List<TreeNode> hierarchy = TreeNode.BuildHierarchy(symbols, _colorer);
		TreeNode.DisplayHierarchy(hierarchy, options);
		println($"\nFound {symbols.Count} symbols total");
	}

	public void CMD_ls_env(bool showValues) {
		trace($"Executing ls-env command with showValues: {showValues}");
		println("Environment file detection and loading trace:");
		println();

		EnvLoader.EnvLoadResult result = EnvLoader.LoadEnvironmentFiles();
		EnvLoader.PrintLoadTrace(result, showValues);
		println();
		println($"Environment variables successfully loaded and available for configuration.");
	}

	public async Task CMD_ls_cache(string pattern, bool showKeys, bool showAll) {
		trace($"Executing ls-cache command with pattern: '{pattern}', keys: {showKeys}, all: {showAll}");
		string[] args = BuildArgsForLsCache(pattern, showKeys, showAll);
		await CMD_ls_cache(args);
	}

	public async Task CMD_try_lsp(bool showAll, bool cleanup) {
		trace($"Executing ls-lsp command with showAll: {showAll}, cleanup: {cleanup}");
		string[] args = BuildArgsForLsLsp(showAll, cleanup);
		await CMD_try_lsp(args);
	}

	public async Task CMD_try(string filePath, string symbolName, string? promptName, bool interactive, int nRollouts) {
		trace($"Executing try command: {filePath}::{symbolName}, prompt: {promptName}, interactive: {interactive}, n: {nRollouts}");
		string[] args = BuildArgsForTry(filePath, symbolName, promptName, interactive, nRollouts);
		await CMD_try(args);
	}

	public async Task CMD_optimize(string path, string language, string compression, bool endgame) {
		trace($"Executing optimize command: {path}, {language}, {compression}, endgame: {endgame}");

		// Convert to options
		var compressionLevel = endgame ? CompressionLevel.Endgame : ParseCompressionLevel(compression);
		var options          = new CompressorOptions(path, DetectLanguageInternal(path, language), compressionLevel);

		println($"Starting hierarchical optimization of {options.ProjectPath} ({options.Language})...");
		println();

		try {
			DateTime        startTime = DateTime.UtcNow;
			SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(options.ProjectPath, options.Language, options.CompressionLevel);
			TimeSpan        duration  = DateTime.UtcNow - startTime;

			// Display extracted keys
			traceheader("EXTRACTED KEYS");
			foreach (KeyValuePair<string, string> key in hierarchy.ExtractedKeys) {
				traceln(key.Key, key.Value.Length > 80 ? $"{key.Value[..77]}..." : key.Value, "KEY");
			}

			traceheader("OPTIMIZATION COMPLETE");
			traceln("Duration", $"{duration.TotalSeconds:F2} seconds", "TIME");
			traceln("Root Symbols", $"{hierarchy.RootSymbols.Count} symbols", "COUNT");
			traceln("Keys Generated", $"{hierarchy.ExtractedKeys.Count} keys", "COUNT");
			println();
			println("Hierarchical optimization completed successfully!");
		} catch (Exception ex) {
			println($"Error during optimization: {ex.Message}");
			_logger.LogError(ex, "Optimization failed");
			Environment.Exit(1);
		}
	}

	private string DetectLanguageInternal(string projectPath, string language) {
		if (language != "auto") return language;
		return DetectLanguage(projectPath);
	}

	private CompressionLevel ParseCompressionLevel(string level) {
		return level.ToLowerInvariant() switch {
			"optimize" => CompressionLevel.Optimize,
			"compress" => CompressionLevel.Compress,
			"golf"     => CompressionLevel.Golf,
			"endgame"  => CompressionLevel.Endgame,
			_          => CompressionLevel.Compress
		};
	}

	// Helper methods to build args for legacy methods
	private string[] BuildArgsForLsCache(string pattern, bool showKeys, bool showAll) {
		var args = new List<string> { "ls-cache" };
		if (!string.IsNullOrEmpty(pattern)) args.Add(pattern);
		if (showKeys) args.AddRange(["--keys"]);
		if (showAll) args.AddRange(["--all"]);
		return args.ToArray();
	}

	private string[] BuildArgsForLsLsp(bool showAll, bool cleanup) {
		var args = new List<string> { "ls-lsp" };
		if (showAll) args.AddRange(["--all"]);
		if (cleanup) args.AddRange(["--cleanup"]);
		return args.ToArray();
	}

	private string[] BuildArgsForTry(string filePath, string symbolName, string? promptName, bool interactive, int nRollouts) {
		var args = new List<string> { "try", filePath, symbolName };
		if (!string.IsNullOrEmpty(promptName)) args.AddRange(["--prompt", promptName]);
		if (interactive) args.Add("--interactive");
		if (nRollouts != 1) args.AddRange(["--n", nRollouts.ToString()]);
		return args.ToArray();
	}

	private async Task HandleAssemblyCommand(string assemblyName, LsOptions options) {
		// Load and handle assembly listing
		var assemblies = AppDomain.CurrentDomain.GetAssemblies();
		var matchedAssembly = assemblies.FirstOrDefault(a =>
			a.GetName().Name?.Contains(assemblyName, StringComparison.OrdinalIgnoreCase) == true);

		if (matchedAssembly != null) {
			await CMD_ls_assembly(matchedAssembly, options);
		} else {
			println($"Assembly '{assemblyName}' not found.");
			println("\nAvailable loaded assemblies:");
			foreach (var asm in assemblies.OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				println($"  - {name}");
			}
		}
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
}