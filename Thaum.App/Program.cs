using Ratatui;
using Serilog;
using Thaum.Core;
using static Thaum.Core.Utils.Tracer;

namespace Thaum;

public static class Program {
	public static async Task Main(string[] args) {
		// Clear the output log file on startup
		if (File.Exists(GLB.OutputLogFile)) {
			File.Delete(GLB.OutputLogFile);
		}

		// Configure Serilog
		RatLog.SetupCLI();

		// Check if CLI arguments provided
		if (args.Length > 0) {
			CLI.CLI cliApp = new CLI.CLI();
			try {
				await cliApp.RunAsync(args);
			} catch (Exception ex) {
				// Log to both console and file
				string errorMsg = $"Error: {ex.Message}";
				string stackMsg = $"Stack trace: {ex.StackTrace}";

				println(errorMsg);
				println(stackMsg);

				// Also log to Serilog file
				Log.Fatal(ex, "Application crashed");
				Log.Information("Error details: {ErrorMessage}", errorMsg);
				Log.Information("Stack trace: {StackTrace}", stackMsg);

				Environment.Exit(1);
			} finally {
				// Ensure trace logger resources are cleaned up
				Dispose();
				// Ensure Serilog flushes and disposes properly
				Log.CloseAndFlush();
			}
		} else {
			println("TUI mode not implemented yet. Use CLI commands.");
			println("Run 'dotnet run help' for usage information.");
		}
	}
}