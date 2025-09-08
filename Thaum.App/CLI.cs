using Microsoft.Extensions.Logging;
using System.CommandLine;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using static Thaum.Core.Utils.Tracer;
using Thaum.Core;
using Thaum.Utils;
using Command = System.CommandLine.Command;

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
		var rootCommand = CreateRootCommand(this);

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

	/// <summary>
	/// Creates root command with all subcommands configured where System.CommandLine handles
	/// parsing/validation/help generation where commands maintain semantic hierarchy
	/// </summary>
	public static RootCommand CreateRootCommand(CLI cli) {
		var root = new RootCommand("Thaum - Hierarchical Compression Engine");

		root.Subcommands.Add(CreateLsCommand(cli));
		root.Subcommands.Add(CreateLsEnvCommand(cli));
		root.Subcommands.Add(CreateLsCacheCommand(cli));
		root.Subcommands.Add(CreateLsLspCommand(cli));
        root.Subcommands.Add(CreateTryCommand(cli));
        root.Subcommands.Add(CreateOptimizeCommand(cli));
        root.Subcommands.Add(CreateEvalCommand(cli));
        root.Subcommands.Add(CreateEvalCompressionCommand(cli));
        root.Subcommands.Add(CreateTuiCommand(cli));

		return root;
	}

	/// <summary>
	/// ls command: List symbols in hierarchical format
	/// </summary>
	private static Command CreateLsCommand(CLI cli) {
		var argPath = new Argument<string>("path") {
			Description         = "Project path or assembly specifier (e.g., 'assembly:TreeSitter')",
			DefaultValueFactory = _ => Directory.GetCurrentDirectory()
		};

		var optLang = new Option<string>("--lang") {
			Description         = "Programming language",
			DefaultValueFactory = _ => "auto"
		};

		var optDepth = new Option<int>("--depth") {
			Description         = "Maximum nesting depth",
			DefaultValueFactory = _ => 10
		};

		var optTypes = new Option<bool>("--types") {
			Description = "Show symbol types"
		};
		var optNoColors = new Option<bool>("--no-colors") {
			Description = "Disable colored output"
		};

		var cmd = new Command("ls", "List symbols in hierarchical format");
		cmd.Arguments.Add(argPath);
		cmd.Options.Add(optLang);
		cmd.Options.Add(optDepth);
		cmd.Options.Add(optTypes);
		cmd.Options.Add(optNoColors);

		cmd.SetAction(async (parseResult, cancellationToken) => {
			var path     = parseResult.GetValue(argPath)!;
			var lang     = parseResult.GetValue(optLang)!;
			var depth    = parseResult.GetValue(optDepth);
			var types    = parseResult.GetValue(optTypes);
			var noColors = parseResult.GetValue(optNoColors);

			var options = new LsOptions(path, lang, depth, types, noColors);
			await cli.CMD_ls(options);
		});

		return cmd;
	}

	/// <summary>
	/// ls-env command: Show environment file detection and merging
	/// </summary>
	private static Command CreateLsEnvCommand(CLI cli) {
		var optValues = new Option<bool>("--values", "-v") {
			Description = "Show actual environment variable values"
		};

		var cmd = new Command("ls-env", "Show .env file detection and merging trace");
		cmd.Options.Add(optValues);

		cmd.SetAction((parseResult, _) => {
			var values = parseResult.GetValue(optValues);
			cli.CMD_ls_env(values);
			return Task.CompletedTask;
		});

		return cmd;
	}

	/// <summary>
	/// ls-cache command: Browse cached symbol compressions
	/// </summary>
	private static Command CreateLsCacheCommand(CLI cli) {
		var patternArg = new Argument<string>("pattern") {
			Description         = "Filter cached symbols by pattern",
			DefaultValueFactory = _ => ""
		};

		var optKeys = new Option<bool>("--keys", "-k") { Description = "Show K1/K2 architectural keys" };
		var optAll  = new Option<bool>("--all", "-a") { Description  = "Show both optimizations and keys" };

		var cmd = new Command("ls-cache", "Browse cached symbol compressions");
		cmd.Arguments.Add(patternArg);
		cmd.Options.Add(optKeys);
		cmd.Options.Add(optAll);

		cmd.SetAction(async (parseResult, _) => {
			var pattern = parseResult.GetValue(patternArg)!;
			var keys    = parseResult.GetValue(optKeys);
			var all     = parseResult.GetValue(optAll);

			await cli.CMD_ls_cache(pattern, keys, all);
		});

		return cmd;
	}

	/// <summary>
	/// ls-lsp command: Manage auto-downloaded LSP servers
	/// </summary>
	private static Command CreateLsLspCommand(CLI cli) {
		var optAll     = new Option<bool>("--all", "-a") { Description     = "Show detailed information about cached servers" };
		var optCleanup = new Option<bool>("--cleanup", "-c") { Description = "Remove old LSP server versions" };

		var cmd = new Command("ls-lsp", "Manage auto-downloaded LSP servers");
		cmd.Options.Add(optAll);
		cmd.Options.Add(optCleanup);

		cmd.SetAction(async (parseResult, cancellationToken) => {
			var all     = parseResult.GetValue(optAll);
			var cleanup = parseResult.GetValue(optCleanup);

			await cli.CMD_try_lsp(all, cleanup);
		});

		return cmd;
	}

	/// <summary>
	/// try command: Test prompts on individual symbols
	/// </summary>
    private static Command CreateTryCommand(CLI cli) {
        // Accept either combined path-spec (<file>::<symbol>) or split args (<file> <symbol>)
        var argPathSpec  = new Argument<string>("file-or-pathspec") { Description = "Either '<file>::<symbol>' or '<file> <symbol>'" };
        var argSymbolOpt = new Argument<string?>("symbol-name")
        {
            Description = "Name of symbol to test (omit if using '<file>::<symbol>')",
            Arity       = ArgumentArity.ZeroOrOne
        };
        var optPrompt      = new Option<string?>("--prompt") { Description = "Prompt file name (e.g., compress_function_v5)" };
        var optDry         = new Option<bool>("--dry") { Description = "Do not call LLM; print and save constructed prompt only" };
        var optInteractive = new Option<bool>("--interactive") { Description = "Launch interactive TUI with live updates" };
        var optN = new Option<int>("--n") {
            Description         = "Number of rollouts for fusion",
            DefaultValueFactory = _ => 1
        };

        var cmd = new Command("try", "Test prompts on individual symbols");
        cmd.Arguments.Add(argPathSpec);
        cmd.Arguments.Add(argSymbolOpt);
        cmd.Options.Add(optPrompt);
        cmd.Options.Add(optInteractive);
        cmd.Options.Add(optDry);
        cmd.Options.Add(optN);

        cmd.SetAction(async (parseResult, cancellationToken) => {
            var pathSpec    = parseResult.GetValue(argPathSpec)!;
            var symbolArg   = parseResult.GetValue(argSymbolOpt);
            var prompt      = parseResult.GetValue(optPrompt);
            var interactive = parseResult.GetValue(optInteractive);
            var dryRun      = parseResult.GetValue(optDry);
            var n           = parseResult.GetValue(optN);

            string file;
            string symbol;
            if (pathSpec.Contains("::")) {
                var parts = pathSpec.Split("::", 2);
                file   = parts[0];
                symbol = parts.Length > 1 ? parts[1] : string.Empty;
            } else {
                file   = pathSpec;
                symbol = symbolArg ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(symbol)) {
                Console.WriteLine("Error: Provide '<file>::<symbol>' or '<file> <symbol>'");
                return;
            }

            await cli.CMD_try(file, symbol, prompt, interactive, n, dryRun);
        });

        return cmd;
    }

	/// <summary>
	/// optimize command: Generate codebase optimizations
	/// </summary>
    private static Command CreateOptimizeCommand(CLI cli) {
		var optPath = new Option<string>("--path") {
			Description         = "Project path",
			DefaultValueFactory = _ => Directory.GetCurrentDirectory()
		};

		var optLang = new Option<string>("--lang") {
			Description         = "Programming language",
			DefaultValueFactory = _ => "auto"
		};

		var optPrompt = new Option<string?>("--prompt", "-p") {
			Description = "Prompt template name (e.g., compress_function_v5, optimize_class, golf_function)"
		};

		var optEndgame = new Option<bool>("--endgame") {
			Description = "Use maximum endgame compression"
		};

		var cmd = new Command("optimize", "Generate codebase optimizations");
		cmd.Options.Add(optPath);
		cmd.Options.Add(optLang);
		cmd.Options.Add(optPrompt);
		cmd.Options.Add(optEndgame);

		cmd.SetAction(async (parseResult, cancellationToken) => {
			var path    = parseResult.GetValue(optPath)!;
			var lang    = parseResult.GetValue(optLang)!;
			var prompt  = parseResult.GetValue(optPrompt);
			var endgame = parseResult.GetValue(optEndgame);

			await cli.CMD_optimize(path, lang, prompt, endgame);
		});

		return cmd;
    }

    private static Command CreateEvalCommand(CLI cli) {
        var argFile   = new Argument<string>("file-path") { Description = "Path to source file" };
        var argSymbol = new Argument<string>("symbol-name") { Description = "Name of symbol to evaluate" };
        var optTriad  = new Option<string?>("--triad") { Description = "Path to triad JSON (optional)" };

        var cmd = new Command("eval", "Evaluate compression fidelity for a single function triad");
        cmd.Arguments.Add(argFile);
        cmd.Arguments.Add(argSymbol);
        cmd.Options.Add(optTriad);

        cmd.SetAction(async (parseResult, cancellationToken) => {
            var file   = parseResult.GetValue(argFile)!;
            var symbol = parseResult.GetValue(argSymbol)!;
            var triad  = parseResult.GetValue(optTriad);

            await cli.CMD_eval(file, symbol, triad);
        });

        return cmd;
    }

    private static Command CreateEvalCompressionCommand(CLI cli) {
        var optPath = new Option<string>("--path") {
            Description = "Root directory to evaluate",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory()
        };
        var optLang = new Option<string>("--lang") {
            Description = "Programming language (or 'auto')",
            DefaultValueFactory = _ => "auto"
        };
        var optOut = new Option<string?>("--out") { Description = "CSV output path (optional)" };
        var optJson = new Option<string?>("--json") { Description = "JSON output path (optional)" };

        var cmd = new Command("eval-compression", "Batch evaluation across a directory");
        cmd.Options.Add(optPath);
        cmd.Options.Add(optLang);
        cmd.Options.Add(optOut);
        cmd.Options.Add(optJson);

        cmd.SetAction(async (parseResult, cancellationToken) => {
            var path = parseResult.GetValue(optPath)!;
            var lang = parseResult.GetValue(optLang)!;
            var outp = parseResult.GetValue(optOut);
            var json = parseResult.GetValue(optJson);
            await cli.CMD_eval_compression(path, lang, outp, json);
        });

        return cmd;
    }
	/// <summary>
	/// tui command: Launch interactive symbol browser
	/// </summary>
	private static Command CreateTuiCommand(CLI cli) {
		var optPath = new Option<string>("--path", "-p") {
			Description         = "Project path to browse",
			DefaultValueFactory = _ => Directory.GetCurrentDirectory()
		};

		var optLang = new Option<string>("--lang", "-l") {
			Description         = "Programming language",
			DefaultValueFactory = _ => "auto"
		};

		var cmd = new Command("tui", "Launch interactive symbol browser");
		cmd.Options.Add(optPath);
		cmd.Options.Add(optLang);

		cmd.SetAction(async (parseResult, cancellationToken) => {
			var path = parseResult.GetValue(optPath)!;
			var lang = parseResult.GetValue(optLang)!;

			await cli.CMD_tui(path, lang);
		});

		return cmd;
	}
}
