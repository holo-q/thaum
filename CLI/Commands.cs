using System.CommandLine;
using Microsoft.Extensions.Logging;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using Thaum.Utils;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

/// <summary>
/// System.CommandLine command definitions where each command encapsulates arguments/options
/// where automatic help generation maintains synchronization where type safety prevents errors
/// where fluent API creates clean composition eliminating hand-rolled parsing complexity
/// </summary>
public static class CLI_Commands {
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

		return root;
	}

	/// <summary>
	/// ls command: List symbols in hierarchical format
	/// </summary>
	private static Command CreateLsCommand(CLI cli) {
		var pathArg = new Argument<string>("path") {
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
		cmd.Arguments.Add(pathArg);
		cmd.Options.Add(optLang);
		cmd.Options.Add(optDepth);
		cmd.Options.Add(optTypes);
		cmd.Options.Add(optNoColors);

		cmd.SetAction(async (parseResult, cancellationToken) => {
			var path     = parseResult.GetValue(pathArg)!;
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

		cmd.SetAction((parseResult, cancellationToken) => {
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

		var optKeys = new Option<bool>("--keys", "-k") {
			Description = "Show K1/K2 architectural keys"
		};
		var optAll = new Option<bool>("--all", "-a") {
			Description = "Show both optimizations and keys"
		};

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
		var optAll = new Option<bool>("--all", "-a") {
			Description = "Show detailed information about cached servers"
		};
		var optCleanup = new Option<bool>("--cleanup", "-c") {
			Description = "Remove old LSP server versions"
		};

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
		var fileArg = new Argument<string>("file-path") {
			Description = "Path to source file"
		};
		var symbolArg = new Argument<string>("symbol-name") {
			Description = "Name of symbol to test"
		};
		var optPrompt = new Option<string?>("--prompt") {
			Description = "Prompt file name (e.g., compress_function_v2, endgame_function)"
		};
		var optInteractive = new Option<bool>("--interactive") {
			Description = "Launch interactive TUI with live updates"
		};
		var optN = new Option<int>("--n") {
			Description         = "Number of rollouts for fusion",
			DefaultValueFactory = _ => 1
		};

		var cmd = new Command("try", "Test prompts on individual symbols");
		cmd.Arguments.Add(fileArg);
		cmd.Arguments.Add(symbolArg);
		cmd.Options.Add(optPrompt);
		cmd.Options.Add(optInteractive);
		cmd.Options.Add(optN);

		cmd.SetAction(async (parseResult, cancellationToken) => {
			var file        = parseResult.GetValue(fileArg)!;
			var symbol      = parseResult.GetValue(symbolArg)!;
			var prompt      = parseResult.GetValue(optPrompt);
			var interactive = parseResult.GetValue(optInteractive);
			var n           = parseResult.GetValue(optN);

			await cli.CMD_try(file, symbol, prompt, interactive, n);
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
}