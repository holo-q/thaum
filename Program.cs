using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using Thaum.CLI;
using Thaum.Core.Utils;

namespace Thaum;

public static class Program {
	public static async Task Main(string[] args) {
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
				Console.WriteLine($"Error: {ex.Message}");
				Console.WriteLine($"Stack trace: {ex.StackTrace}");
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