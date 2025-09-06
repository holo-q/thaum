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
	private readonly Crawler       _crawler;
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
				ln($"Unknown command: {command}");
				CMD_help();
				Environment.Exit(1);
				break;
		}

		traceout();
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