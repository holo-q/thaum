using static System.Console;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing help command implementation where documentation displays
/// available commands where examples guide usage where options reveal capabilities
/// where environment variables enable configuration
/// </summary>
public partial class CLI {
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