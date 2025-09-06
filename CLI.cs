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
	private readonly ILogger<CLI>      _logger;
	private readonly LLM               _llm;
	private readonly Crawler           _crawler;
	private readonly Prompter          _prompter;
	private readonly Compressor        _compressor;
	private readonly PerceptualColorer _colorer;

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

	// All command implementations have been moved to their respective CLI_*.cs files
}