using Microsoft.Extensions.Logging;
using static Thaum.Core.Utils.Tracer;
using System.Diagnostics.CodeAnalysis;
using Thaum.App.RatatuiTUI;
using Thaum.Core.Models;

namespace Thaum.CLI;

/// <summary>
/// TUI command implementation using Ratatui-style rendering.
/// Fully removes Terminal.Gui and drives a split-view console UI.
/// </summary>
public partial class CLI {
	[RequiresUnreferencedCode("Uses reflection for TUI component initialization")]
	public async Task CMD_tui(string projectPath, string language) {
		trace($"Launching TUI symbol browser for path: {projectPath}, language: {language}");

		try {
			// Initialize symbols from project
			println($"Loading symbols from {projectPath}...");
			CodeMap codeMap = await _crawler.CrawlDir(projectPath);

			if (codeMap.Count == 0) {
				println("No symbols found in the specified path.");
				return;
			}

			println($"Found {codeMap.Count} symbols across {codeMap.FileCount} files");
			println("Starting interactive symbol browser...");

			// Run the new Ratatui-based TUI
			ThaumTUI app = new ThaumTUI(projectPath, codeMap, _crawler, _golfer);
			await app.RunAsync();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error launching TUI symbol browser");
			println($"Error: {ex.Message}");
		}
	}
}
