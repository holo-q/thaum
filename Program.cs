using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Thaum.CLI;
using Thaum.Core.Utils;

namespace Thaum;

public static class Program {
	public static async Task Main(string[] args) {
		// Clear the output log file on startup
		if (File.Exists("output.log")) {
			File.Delete("output.log");
		}
		
		// Configure Serilog
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Console()
			.WriteTo.File("output.log", 
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.WriteTo.Seq("http://localhost:5341")
			.CreateLogger();

		ILoggerFactory loggerFactory = new SerilogLoggerFactory(Log.Logger);
		var logger = loggerFactory.CreateLogger<CliApplication>();

		// Check if CLI arguments provided
		if (args.Length > 0) {
			CliApplication cliApp = new CliApplication(logger);
			try {
				await cliApp.RunAsync(args);
			} catch (Exception ex) {
				// Log to both console and file
				var errorMsg = $"Error: {ex.Message}";
				var stackMsg = $"Stack trace: {ex.StackTrace}";
				
				Console.WriteLine(errorMsg);
				Console.WriteLine(stackMsg);
				
				// Also log to Serilog file
				Log.Fatal(ex, "Application crashed");
				Log.Information("Error details: {ErrorMessage}", errorMsg);
				Log.Information("Stack trace: {StackTrace}", stackMsg);
				
				Environment.Exit(1);
			} finally {
				// Ensure trace logger resources are cleaned up
				TraceLogger.Dispose();
				// Ensure Serilog flushes and disposes properly
				Log.CloseAndFlush();
			}
		} else {
			Console.WriteLine("TUI mode not implemented yet. Use CLI commands.");
			Console.WriteLine("Run 'dotnet run help' for usage information.");
		}
	}
}