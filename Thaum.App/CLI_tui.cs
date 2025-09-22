using Microsoft.Extensions.Logging;
using static Thaum.Core.Utils.Tracer;
using System.Diagnostics.CodeAnalysis;
using Ratatui.Reload;
using Thaum.Core.Utils;

namespace Thaum.CLI;

/// <summary>
/// TUI command implementation using Ratatui-style rendering.
/// Fully removes Terminal.Gui and drives a split-view console UI.
/// </summary>
public partial class CLI {
	[RequiresUnreferencedCode("Uses reflection for TUI component initialization")]
    public async Task CMD_tui(string projectPath, string language) {
        // Switch logging to TUI mode to avoid console writes that would corrupt the frame buffer
        Logging.SetupTUI();
        trace($"Launching TUI symbol browser for path: {projectPath}, language: {language}");

        try {
            // Always launch via live reload host; in consumer builds it runs single-shot without watching
            // TODO a better solution would be to
            string csproj = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Thaum.TUI", "Thaum.TUI.csproj"));
            if (!File.Exists(csproj)) {
                println($"Plugin project not found: {csproj}");
                return;
            }

            using RatHost runner = new RatHost(csproj, configuration: "Debug");
            await runner.RunAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "Error launching TUI symbol browser");
            println($"Error: {ex.Message}");
        }
    }
}
