using Serilog;
using Thaum.Core.Utils;
using Thaum.Utils;
using static Thaum.Core.Utils.Tracer;

namespace Thaum;

public static class Program {
	public static async Task Main(string[] args) {
		// Clear the output log file on startup
		if (File.Exists("output.log")) {
			File.Delete("output.log");
		}

		// Configure Serilog
		Logging.SetupCLI();

		// Check if CLI arguments provided
		if (args.Length > 0) {
			CLI.CLI cliApp = new CLI.CLI();
			try {
				await cliApp.RunAsync(args);
			} catch (Exception ex) {
				// Log to both console and file
				var errorMsg = $"Error: {ex.Message}";
				var stackMsg = $"Stack trace: {ex.StackTrace}";

				ln(errorMsg);
				ln(stackMsg);

				// Also log to Serilog file
				Log.Fatal(ex, "Application crashed");
				Log.Information("Error details: {ErrorMessage}", errorMsg);
				Log.Information("Stack trace: {StackTrace}", stackMsg);

				Environment.Exit(1);
			} finally {
				// Ensure trace logger resources are cleaned up
				Tracer.Dispose();
				// Ensure Serilog flushes and disposes properly
				Log.CloseAndFlush();
			}
		} else {
			ln("TUI mode not implemented yet. Use CLI commands.");
			ln("Run 'dotnet run help' for usage information.");
		}
	}
}