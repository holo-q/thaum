using Thaum.Utils;
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
	public void CMD_ls_env(bool showValues) {
		trace($"Executing ls-env command with showValues: {showValues}");
		println("Environment file detection and loading trace:");
		println();

		EnvLoader.EnvLoadResult result = EnvLoader.LoadEnvironmentFiles();
		EnvLoader.PrintLoadTrace(result, showValues);
		println();
		println($"Environment variables successfully loaded and available for configuration.");
	}
}