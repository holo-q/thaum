using Microsoft.Extensions.Logging;
using McMaster.Extensions.CommandLineUtils;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using static Thaum.Core.Utils.Tracer;
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

		_crawler = new TreeSitterCrawler();
		_logger  = Logging.For<CLI>();
		_colorer = new PerceptualColorer();

		// Initialize trace logger first
		Initialize(_logger);
		tracein();

		// Load .env files before constructing components that read configuration
		EnvLoader.LoadAndApply();

		// Now construct LLM and dependent services with environment-applied configuration
		_llm      = new HttpLLM(httpClient, GLB.AppConfig);
		_prompter = new Prompter(_llm);

		Cache        cache        = new Cache(GLB.AppConfig);
		PromptLoader promptLoader = new PromptLoader();

		_compressor = new Compressor(_llm, _crawler, cache, promptLoader);
		traceout();
	}

	/// <summary>
	/// Main entry point now using McMaster.Extensions.CommandLineUtils where commands
	/// are defined fluently with automatic help and sensible defaults.
	/// </summary>
	public async Task RunAsync(string[] args) {
		tracein(parameters: new { args = string.Join(" ", args) });
		using var scope = trace_scope("RunAsync");

		var app = new CommandLineApplication();
		app.Name        = "thaum";
		app.Description = "Thaum - Hierarchical Compression Engine";
		app.HelpOption(inherited: true);

		ConfigureCommands(app, this);

		// Default to help when no args provided
		app.OnExecute(() => {
			trace("No arguments provided, showing help");
			app.ShowHelp();
			return 0;
		});

		trace($"Processing command with CommandLineUtils: {string.Join(" ", args)}");
		int result = await app.ExecuteAsync(args);
		trace($"Command completed with exit code: {result}");
		traceout();
	}

	/// <summary>
	/// Configure root and subcommands using McMaster.CommandLineUtils.
	/// </summary>
	private static void ConfigureCommands(CommandLineApplication app, CLI cli) {
		app.Command("ls", cmd => ConfigureLs(cmd, cli));
		app.Command("ls-env", cmd => ConfigureLsEnv(cmd, cli));
		app.Command("ls-cache", cmd => ConfigureLsCache(cmd, cli));
		app.Command("optimize", cmd => ConfigureOptimize(cmd, cli));
		app.Command("eval-compression", cmd => ConfigureEvalCompression(cmd, cli));
		app.Command("compress-batch", cmd => ConfigureCompressBatch(cmd, cli));
		app.Command("tui", cmd => ConfigureTui(cmd, cli));
		app.Command("ls-lsp", cmd => {
			cmd.Description = "(Temporarily disabled during TUI migration)";
			cmd.OnExecute(() => {
				Console.WriteLine("ls-lsp is temporarily disabled.");
				return 0;
			});
		});
		app.Command("try", cmd => {
			cmd.Description = "(Temporarily disabled during TUI migration)";
			cmd.OnExecute(() => {
				Console.WriteLine("try is temporarily disabled.");
				return 0;
			});
		});
	}

	/// <summary>
	/// ls command: List symbols in hierarchical format
	/// </summary>
	private static void ConfigureLs(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "List symbols or, if batchJson is provided, print triads from a batch report";
		cmd.HelpOption(inherited: true);

		var argPath     = cmd.Argument("path", "Project path or assembly specifier (e.g., 'assembly:TreeSitter')");
		var argBatch    = cmd.Argument("batchJson", "Optional path to eval JSON (report.json) to print matched triads");
		var optLang     = cmd.Option("--lang", "Programming language", CommandOptionType.SingleValue);
		var optDepth    = cmd.Option("--depth", "Maximum nesting depth", CommandOptionType.SingleValue);
		var optTypes    = cmd.Option("--types", "Show symbol types", CommandOptionType.NoValue);
		var optNoColors = cmd.Option("--no-colors", "Disable colored output", CommandOptionType.NoValue);
		var optBatch    = cmd.Option("--batch", "(Deprecated) Path to eval JSON (report.json) to print matched triads", CommandOptionType.SingleValue);
		var optSplit    = cmd.Option("--split", "Align triad fragments at mid-column (50%)", CommandOptionType.NoValue);

		cmd.OnExecuteAsync(async cancellationToken => {
			string path                                          = string.IsNullOrWhiteSpace(argPath.Value) ? Directory.GetCurrentDirectory() : argPath.Value!;
			string lang                                          = string.IsNullOrWhiteSpace(optLang.Value()) ? "auto" : optLang.Value()!;
			int    depth                                         = 10;
			if (int.TryParse(optDepth.Value(), out var d)) depth = d;
			bool    types                                        = optTypes.HasValue();
			bool    noColors                                     = optNoColors.HasValue();
			string? batchPos                                     = string.IsNullOrWhiteSpace(argBatch.Value) ? null : argBatch.Value;
			string? batchOpt                                     = string.IsNullOrWhiteSpace(optBatch.Value()) ? null : optBatch.Value();
			string? batch                                        = string.IsNullOrWhiteSpace(batchPos) ? batchOpt : batchPos; // positional overrides
			bool    split                                        = optSplit.HasValue();

			var options = new LsOptions(path, lang, depth, types, noColors, batch, split);
			await cli.HandleLs(options);
			return 0;
		});
	}

	/// <summary>
	/// ls-env command: Show environment file detection and merging
	/// </summary>
	private static void ConfigureLsEnv(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "Show .env file detection and merging trace";
		cmd.HelpOption(inherited: true);
		var optValues = cmd.Option("-v|--values", "Show actual environment variable values", CommandOptionType.NoValue);
		cmd.OnExecute(() => {
			cli.CMD_ls_env(optValues.HasValue());
			return 0;
		});
	}

	/// <summary>
	/// ls-cache command: Browse cached symbol compressions
	/// </summary>
	private static void ConfigureLsCache(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "Browse cached symbol compressions";
		cmd.HelpOption(inherited: true);
		var argPattern = cmd.Argument("pattern", "Filter cached symbols by pattern");
		var optKeys    = cmd.Option("-k|--keys", "Show K1/K2 architectural keys", CommandOptionType.NoValue);
		var optAll     = cmd.Option("-a|--all", "Show both optimizations and keys", CommandOptionType.NoValue);
		cmd.OnExecuteAsync(async _ => {
			string pattern = argPattern.Value ?? string.Empty;
			bool   keys    = optKeys.HasValue();
			bool   all     = optAll.HasValue();
			await cli.CMD_ls_cache(pattern, keys, all);
			return 0;
		});
	}

	/// <summary>
	/// ls-lsp command: Manage auto-downloaded LSP servers
	/// </summary>
	// handled inline in ConfigureCommands

	/// <summary>
	/// try command: Test prompts on individual symbols
	/// </summary>
	// handled inline in ConfigureCommands

	/// <summary>
	/// optimize command: Generate codebase optimizations
	/// </summary>
	private static void ConfigureOptimize(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "Generate codebase optimizations";
		cmd.HelpOption(inherited: true);
		var optPath    = cmd.Option("--path", "Project path", CommandOptionType.SingleValue);
		var optLang    = cmd.Option("--lang", "Programming language", CommandOptionType.SingleValue);
		var optPrompt  = cmd.Option("-p|--prompt", "Prompt template name (e.g., compress_function_v5, optimize_class, golf_function)", CommandOptionType.SingleValue);
		var optEndgame = cmd.Option("--endgame", "Use maximum endgame compression", CommandOptionType.NoValue);

		cmd.OnExecuteAsync(async _ => {
			string  path    = string.IsNullOrWhiteSpace(optPath.Value()) ? Directory.GetCurrentDirectory() : optPath.Value()!;
			string  lang    = string.IsNullOrWhiteSpace(optLang.Value()) ? "auto" : optLang.Value()!;
			string? prompt  = string.IsNullOrWhiteSpace(optPrompt.Value()) ? null : optPrompt.Value();
			bool    endgame = optEndgame.HasValue();
			await cli.CMD_optimize(path, lang, prompt, endgame);
			return 0;
		});
	}

	// eval (disabled) handled inline in ConfigureCommands if needed later

	private static void ConfigureEvalCompression(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "Batch evaluation across a directory";
		cmd.HelpOption(inherited: true);
		var argPath       = cmd.Argument("path", "Root directory to evaluate");
		var argLang       = cmd.Argument("language", "Programming language (or 'auto')");
		var optOut        = cmd.Option("--out", "CSV output path (optional)", CommandOptionType.SingleValue);
		var optJson       = cmd.Option("--json", "JSON output path (optional)", CommandOptionType.SingleValue);
		var optNoTriads   = cmd.Option("--no-triads", "Disable triad loading (source-only baseline)", CommandOptionType.NoValue);
		var optN          = cmd.Option("--n", "Randomly sample N functions across the directory", CommandOptionType.SingleValue);
		var optTriadsFrom = cmd.Option("--triads-from", "Load triads only from this session directory (overrides default scan)", CommandOptionType.SingleValue);
		var optSeed       = cmd.Option("--seed", "Seed for reproducible sampling", CommandOptionType.SingleValue);

		cmd.OnExecuteAsync(async _ => {
			var     path     = string.IsNullOrWhiteSpace(argPath.Value) ? Directory.GetCurrentDirectory() : argPath.Value!;
			var     lang     = string.IsNullOrWhiteSpace(argLang.Value) ? "auto" : argLang.Value!;
			string? outp     = string.IsNullOrWhiteSpace(optOut.Value()) ? null : optOut.Value();
			string? json     = string.IsNullOrWhiteSpace(optJson.Value()) ? null : optJson.Value();
			int?    n        = int.TryParse(optN.Value(), out var nVal) ? nVal : (int?)null;
			bool    noTriads = optNoTriads.HasValue();
			string? triFrom  = string.IsNullOrWhiteSpace(optTriadsFrom.Value()) ? null : optTriadsFrom.Value();
			int?    seed     = int.TryParse(optSeed.Value(), out var sVal) ? sVal : (int?)null;
			await cli.CMD_eval_compression(path, lang, outp, json, n, useTriads: !noTriads, triadsFrom: triFrom, seed: seed);
			return 0;
		});
	}

	private static void ConfigureCompressBatch(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "Batch-generate triads across a directory";
		cmd.HelpOption(inherited: true);
		var argPath            = cmd.Argument("path", "Root directory to compress");
		var argLang            = cmd.Argument("language", "Programming language (or 'auto')");
		var optPrompt          = cmd.Option("--prompt", "Prompt name (default: compress_function_v5)", CommandOptionType.SingleValue);
		var optConcurrency     = cmd.Option("--concurrency", "Max concurrent compressions", CommandOptionType.SingleValue);
		var optN               = cmd.Option("--n", "Randomly sample N functions across the directory", CommandOptionType.SingleValue);
		var optRetryIncomplete = cmd.Option("--retry-incomplete", "Retries for symbols with incomplete triads", CommandOptionType.SingleValue);
		var optSeed            = cmd.Option("--seed", "Seed for reproducible sampling", CommandOptionType.SingleValue);

		cmd.OnExecuteAsync(async cancellationToken => {
			string  path        = string.IsNullOrWhiteSpace(argPath.Value) ? Directory.GetCurrentDirectory() : argPath.Value!;
			string  lang        = string.IsNullOrWhiteSpace(argLang.Value) ? "auto" : argLang.Value!;
			string? prompt      = string.IsNullOrWhiteSpace(optPrompt.Value()) ? null : optPrompt.Value();
			int     concurrency = int.TryParse(optConcurrency.Value(), out var c) ? c : 4;
			int?    n           = int.TryParse(optN.Value(), out var nVal) ? nVal : (int?)null;
			int     retry       = int.TryParse(optRetryIncomplete.Value(), out var r) ? r : 0;
			int?    seed        = int.TryParse(optSeed.Value(), out var s) ? s : (int?)null;
			await cli.CMD_compress_batch(path, lang, prompt, concurrency, n, cancellationToken, retry, seed);
			return 0;
		});
	}

	/// <summary>
	/// tui command: Launch interactive symbol browser
	/// </summary>
	private static void ConfigureTui(CommandLineApplication cmd, CLI cli) {
		cmd.Description = "Launch interactive symbol browser";
		cmd.HelpOption(inherited: true);
		var optPath = cmd.Option("-p|--path", "Project path to browse", CommandOptionType.SingleValue);
		var optLang = cmd.Option("-l|--lang", "Programming language", CommandOptionType.SingleValue);
		cmd.OnExecuteAsync(async _ => {
			string path = string.IsNullOrWhiteSpace(optPath.Value()) ? Directory.GetCurrentDirectory() : optPath.Value()!;
			string lang = string.IsNullOrWhiteSpace(optLang.Value()) ? "auto" : optLang.Value()!;
			await cli.CMD_tui(path, lang);
			return 0;
		});
	}
}