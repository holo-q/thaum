using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing help command implementation where documentation displays
/// available commands where examples guide usage where options reveal capabilities
/// where environment variables enable configuration
/// </summary>
public partial class CLI {
	private void CMD_help() {
		ln("Thaum - Hierarchical Compression Engine");
		ln();
		ln("Usage:");
		ln("  thaum <command> [options]");
		ln();
		ln("Commands:");
		ln("  ls [path]              List symbols in hierarchical format");
		ln("  ls assembly:<name>     List symbols in loaded assembly (e.g., 'ls assembly:TreeSitter')");
		ln("  ls-env [--values]      Show .env file detection and merging trace");
		ln("  ls-cache [pattern]     Browse cached symbol compressions");
		ln("  ls-lsp [--all] [--cleanup]  Manage auto-downloaded LSP servers");
		ln("  try <file> <symbol>    Test prompts on individual symbols");
		ln("  optimize [path]        Generate codebase optimizations");
		ln("  help                   Show this help message");
		ln();
		ln("Options for 'ls':");
		ln("  --path <path>          Project path (default: current directory)");
		ln("  --lang <language>      Language (python, csharp, javascript, etc.)");
		ln("  --depth <number>       Maximum nesting depth (default: 10)");
		ln("  --types                Show symbol types");
		ln("  --no-colors            Disable colored output");
		ln();
		ln("Options for 'ls-env':");
		ln("  --values, -v           Show actual environment variable values");
		ln();
		ln("Options for 'ls-cache':");
		ln("  [pattern]              Filter cached symbols by pattern");
		ln("  --keys, -k             Show K1/K2 architectural keys");
		ln("  --all, -a              Show both optimizations and keys");
		ln();
		ln("Options for 'ls-lsp':");
		ln("  --all, -a              Show detailed information about cached servers");
		ln("  --cleanup, -c          Remove old LSP server versions");
		ln();
		ln("Options for 'try':");
		ln("  <file_path>            Path to source file");
		ln("  <symbol_name>          Name of symbol to test");
		ln("  --prompt <name>        Prompt file name (e.g., compress_function_v2, endgame_function)");
		ln("  --interactive          Launch interactive TUI with live updates");
		ln();
		ln("Options for 'optimize':");
		ln("  --path <path>          Project path (default: current directory)");
		ln("  --lang <language>      Language (python, csharp, javascript, etc.)");
		ln("  --compression <level>  Compression level: optimize, compress, golf, endgame");
		ln("  -c <level>             Short form of --compression");
		ln("  --endgame              Use maximum endgame compression");
		ln();
		ln("Environment Variables:");
		ln("  THAUM_DEFAULT_FUNCTION_PROMPT  Default prompt for functions (default: compress_function_v2)");
		ln("  THAUM_DEFAULT_CLASS_PROMPT     Default prompt for classes (default: compress_class)");
		ln("  LLM__DefaultModel              LLM model to use for compression");
		ln();
		ln("Available Prompts:");
		ln("  optimize_function, optimize_class, optimize_key");
		ln("  compress_function, compress_function_v2, compress_class, compress_key");
		ln("  golf_function, golf_class, golf_key");
		ln("  endgame_function, endgame_class, endgame_key");
		ln();
		ln("Examples:");
		ln("  thaum ls");
		ln("  thaum ls /path/to/project --lang python --depth 3");
		ln("  thaum ls-cache");
		ln("  thaum ls-cache Handle --keys");
		ln("  thaum try CLI/CliApplication.cs BuildHierarchy");
		ln("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt endgame_function");
		ln("  thaum try CLI/CliApplication.cs BuildHierarchy --interactive");
		ln("  thaum optimize --compression endgame");
		ln("  thaum optimize /path/to/project -c golf");
		ln("  thaum ls-env --values");
		ln();
		ln("Environment Variable Examples:");
		ln("  export THAUM_DEFAULT_FUNCTION_PROMPT=endgame_function");
		ln("  export THAUM_DEFAULT_CLASS_PROMPT=golf_class");
		ln();
		ln("Run without arguments to launch the interactive TUI.");
	}
}