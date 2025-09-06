using Thaum.Core.Utils;
using static System.Console;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing ls-env command implementation where hierarchical .env files
/// merge through directory traversal where environment variables cascade through inheritance
/// where sensitive values display masked for security
/// </summary>
public partial class CLI {
	private static void CMD_ls_env(string[] args) {
		bool showValues = args.Contains("--values") || args.Contains("-v");

		WriteLine("Environment file detection and loading trace:");
		WriteLine();

		EnvLoader.EnvLoadResult result = EnvLoader.LoadEnvironmentFiles();
		EnvLoader.PrintLoadTrace(result, showValues);

		WriteLine();
		WriteLine($"Environment variables successfully loaded and available for configuration.");
	}
}