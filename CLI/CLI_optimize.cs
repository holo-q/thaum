using Microsoft.Extensions.Logging;
using Thaum.CLI.Models;
using Thaum.Core.Models;
using Thaum.Core.Services;
using static System.Console;
using static Thaum.Core.Utils.TraceLogger;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing optimize command implementation where hierarchical compression
/// creates semantic density where K1 emerges from functions where K2 emerges from classes
/// where progressive refinement achieves maximum compression
/// </summary>
public partial class CLI {
	private async Task CMD_optimize(string[] args) {
		CompressorOptions options = ParseSummarizeOptions(args);

		WriteLine($"Starting hierarchical optimization of {options.ProjectPath} ({options.Language})...");
		WriteLine();

		try {
			DateTime        startTime = DateTime.UtcNow;
			SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(options.ProjectPath, options.Language, options.CompressionLevel);
			TimeSpan        duration  = DateTime.UtcNow - startTime;

			// Display extracted keys
			traceheader("EXTRACTED KEYS");
			foreach (KeyValuePair<string, string> key in hierarchy.ExtractedKeys) {
				traceln(key.Key, key.Value.Length > 80 ? $"{key.Value[..77]}..." : key.Value, "KEY");
			}

			traceheader("OPTIMIZATION COMPLETE");
			traceln("Duration", $"{duration.TotalSeconds:F2} seconds", "TIME");
			traceln("Root Symbols", $"{hierarchy.RootSymbols.Count} symbols", "COUNT");
			traceln("Keys Generated", $"{hierarchy.ExtractedKeys.Count} keys", "COUNT");

			WriteLine();
			WriteLine("Hierarchical optimization completed successfully!");
		} catch (Exception ex) {
			WriteLine($"Error during optimization: {ex.Message}");
			_logger.LogError(ex, "Optimization failed");
			Environment.Exit(1);
		}
	}
}