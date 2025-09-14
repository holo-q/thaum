using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ratatui.Reload;
using static Thaum.Core.Utils.Tracer;
using Thaum.Utils;

namespace Thaum.CLI;

public partial class CLI
{
	public async Task CMD_tui_watch(string? pluginProject)
	{
		pluginProject ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Thaum.TUI", "Thaum.TUI.csproj"));
		if (!File.Exists(pluginProject))
		{
			println($"Plugin project not found: {pluginProject}");
			return;
		}

		await using var sp     = new ServiceCollection().BuildServiceProvider();
		var             runner = new HotReloadRunner(_logger, sp, pluginProject, configuration: "Debug");
		using var       cts    = new CancellationTokenSource();

		Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
		await runner.RunAsync(() => (Console.WindowWidth, Console.WindowHeight), cts.Token);
	}
}
