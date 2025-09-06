using Thaum.Core.Utils;
using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing ls-env command implementation where hierarchical .env files
/// merge through directory traversal where environment variables cascade through inheritance
/// where sensitive values display masked for security
/// </summary>
public partial class CLI {
	private static void CMD_ls_env(string[] args) {
		bool showValues = args.Contains("--values") || args.Contains("-v");
		ln("Environment file detection and loading trace:");
		ln();

		EnvLoader.EnvLoadResult result = EnvLoader.LoadEnvironmentFiles();
		EnvLoader.PrintLoadTrace(result, showValues);
		ln();
		ln($"Environment variables successfully loaded and available for configuration.");
	}
}