using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Thaum.Core.Services;
using Thaum.UI.Views;

namespace Thaum.UI;

public class ThaumApplication : IDisposable {
	private readonly Crawler               _crawler;
	private readonly Compressor                _compressor;
	private readonly ILogger<ThaumApplication> _logger;
	private          MainWindow?               _mainWindow;

	public ThaumApplication(
		Crawler               crawler,
		Compressor                compressor,
		ILogger<ThaumApplication> logger) {
		_crawler = crawler;
		_compressor         = compressor;
		_logger             = logger;
	}

    [RequiresUnreferencedCode("Uses reflection for application initialization")]
    public async Task RunAsync() {
		_logger.LogInformation("Starting Thaum application");

		try {
			Application.Init();

			_mainWindow = new MainWindow(_crawler, _compressor, _logger);

			Application.Top.Add(_mainWindow);

			// Global key bindings handled differently in v1.15.0

			Application.Run();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error running Thaum application");
			throw;
		} finally {
			Application.Shutdown();
		}
	}

	// Key handling moved to MainWindow for v1.15.0 compatibility

	private void ShowHelp() {
		Dialog helpDialog = new Dialog() {
			Title = "Help",
			Width = 80,
			Height = 20,
			Modal = true
		};

		TextView helpText = new TextView {
			X        = 1,
			Y        = 1,
			Width    = Dim.Fill(1),
			Height   = Dim.Fill(2),
			ReadOnly = true,
			Text     = GetHelpText()
		};

		Button closeButton = new Button() {
			Text = "Close",
			X = Pos.Center(),
			Y = Pos.Bottom(helpDialog) - 3
		};
		closeButton.Accepting += (sender, e) => helpDialog.RequestStop();

		helpDialog.Add(helpText, closeButton);
		Application.Run(helpDialog);
	}

	private static string GetHelpText() {
		return """
		       Thaum - LSP-Based Codebase Summarization

		       Key Bindings:
		       F1          - Show this help
		       Ctrl+Q      - Quit application
		       Ctrl+O      - Open project
		       Ctrl+S      - Start summarization
		       Ctrl+R      - Refresh symbols
		       Tab         - Navigate between panels
		       Enter       - Select item / Execute action
		       Escape      - Go back / Cancel

		       Features:
		       - Multi-language LSP integration
		       - Hierarchical code summarization
		       - Real-time change detection
		       - LLM-powered analysis
		       - Caching for performance

		       Navigation:
		       Use Tab to move between the project tree, symbol view,
		       and summary panels. Arrow keys navigate within panels.
		       """;
	}

	public void Dispose() {
		Application.Shutdown();
	}
}