using Microsoft.Extensions.Logging;
using Thaum.CLI;

namespace Thaum;

public static class Program {
	public static async Task Main(string[] args) {
		ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
			builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

		// Check if CLI arguments provided
		if (args.Length > 0) {
			CliApplication cliApp = new CliApplication(loggerFactory.CreateLogger<CliApplication>());
			try {
				await cliApp.RunAsync(args);
			} catch (Exception ex) {
				Console.WriteLine($"Error: {ex.Message}");
				Console.WriteLine($"Stack trace: {ex.StackTrace}");
				Environment.Exit(1);
			}
		} else {
			Console.WriteLine("TUI mode not implemented yet. Use CLI commands.");
			Console.WriteLine("Run 'dotnet run help' for usage information.");
		}
	}
}