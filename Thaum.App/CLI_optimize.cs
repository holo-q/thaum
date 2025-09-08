using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing optimize command implementation where hierarchical compression
/// creates semantic density where K1 emerges from functions where K2 emerges from classes
/// where progressive refinement achieves maximum compression
/// </summary>
public partial class CLI {
	public async Task CMD_optimize(string path, string language, string? promptName, bool endgame) {
		trace($"Executing optimize command: {path}, {language}, prompt: {promptName}, endgame: {endgame}");

		// Convert to options - endgame uses endgame prompts, otherwise use specified prompt or default
		string actualPromptName = endgame ? "endgame_function" : promptName;
		var    options          = new CompressorOptions(path, LangUtil.DetectLanguageInternal(path, language), actualPromptName);

		println($"Starting hierarchical optimization of {options.ProjectPath} ({options.Language})...");
		println();

		try {
			DateTime        startTime = DateTime.UtcNow;
			SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(options.ProjectPath, options.Language, options.DefaultPromptName);
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
			println();
			println("Hierarchical optimization completed successfully!");
		} catch (Exception ex) {
			println($"Error during optimization: {ex.Message}");
			_logger.LogError(ex, "Optimization failed");
			Environment.Exit(1);
		}
	}
}