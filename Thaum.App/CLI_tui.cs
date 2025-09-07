using Microsoft.Extensions.Logging;
using Thaum.TUI.Views;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using static Thaum.Core.Utils.Tracer;
using System.Diagnostics.CodeAnalysis;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing TUI command implementation where interactive symbol browser
/// provides navigation through codebase symbols with real-time compression capabilities
/// where Terminal.Gui creates immersive development experience
/// </summary>
public partial class CLI {
	[RequiresUnreferencedCode("Uses reflection for TUI component initialization")]
	public async Task CMD_tui(string projectPath, string language) {
		trace($"Launching TUI symbol browser for path: {projectPath}, language: {language}");

		try {
			// Initialize symbols from project
			println($"Loading symbols from {projectPath}...");
			var codeMap = await _crawler.CrawlDir(projectPath);

			if (codeMap.Count == 0) {
				println("No symbols found in the specified path.");
				return;
			}

			println($"Found {codeMap.Count} symbols across {codeMap.FileCount} files");
			println("Starting interactive symbol browser...");

			// Launch TUI application
			Application.Init();

			try {
				// Use proper v2 Window approach - no need for custom Toplevel wrapper
				var browser = new SymbolBrowserWindowV2(_crawler, _compressor, codeMap, projectPath);
				Application.Run(browser);
			} finally {
				Application.Shutdown();
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error launching TUI symbol browser");
			println($"Error: {ex.Message}");
		}
	}
}