using Microsoft.Extensions.Logging;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using MenuBar = Terminal.Gui.Views.MenuBarv2;
using MenuItem = Terminal.Gui.Views.MenuItemv2;
using MenuBarItem = Terminal.Gui.Views.MenuBarItemv2;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;

namespace Thaum.UI.Views;

public class MainWindow : Window {
	private readonly Crawler    _crawler;
	private readonly Compressor _compressor;
	private readonly ILogger    _logger;

	private readonly MenuBar           _menuBar;
	private readonly SimpleProjectView _projectView;
	private readonly SymbolListView    _symbolList;
	private readonly SummaryView       _summaryView;
	private readonly StatusBar         _statusBar;
	private readonly ProgressView      _progressView;

	private string?          _currentProjectPath;
	private SymbolHierarchy? _currentHierarchy;

	public MainWindow(
		Crawler    crawler,
		Compressor compressor,
		ILogger    logger) : base() {
		Title       = "Thaum - LSP Codebase Summarizer";
		_crawler    = crawler;
		_compressor = compressor;
		_logger     = logger;

		Width  = Dim.Fill();
		Height = Dim.Fill();

		// Create menu bar
		_menuBar = CreateMenuBar();

		// Create main layout
		View mainContainer = new View {
			X      = 0,
			Y      = 1, // Below menu bar
			Width  = Dim.Fill(),
			Height = Dim.Fill(1) // Above status bar
		};

		// Left panel - Project files (30% width)
		_projectView = new SimpleProjectView {
			X      = 0,
			Y      = 0,
			Width  = Dim.Percent(30),
			Height = Dim.Fill()
		};
		_projectView.FileSelected += OnFileSelected;

		// Middle panel - Symbol list (40% width)
		_symbolList = new SymbolListView {
			X      = Pos.Right(_projectView),
			Y      = 0,
			Width  = Dim.Percent(40),
			Height = Dim.Fill()
		};
		_symbolList.SelectionChanged += OnSymbolSelectionChanged;

		// Right panel - Summary view (30% width)
		_summaryView = new SummaryView {
			X      = Pos.Right(_symbolList),
			Y      = 0,
			Width  = Dim.Fill(),
			Height = Dim.Fill()
		};

		// Progress view (initially hidden)
		_progressView = new ProgressView {
			X       = Pos.Center(),
			Y       = Pos.Center(),
			Width   = 60,
			Height  = 10,
			Visible = false,
			Modal   = true
		};

		// Status bar
		_statusBar = new StatusBar([
			new(Key.F1, "~F1~ Help", () => { }),
			new(Key.O.WithCtrl, "~Ctrl+O~ Open", OpenProject),
			new(Key.S.WithCtrl, "~Ctrl+S~ Summarize", StartSummarization),
			new(Key.Q.WithCtrl, "~Ctrl+Q~ Quit", () => Application.RequestStop())
		]);

		mainContainer.Add(_projectView, _symbolList, _summaryView);
		Add(_menuBar, mainContainer, _statusBar, _progressView);
	}

	private MenuBar CreateMenuBar() {
		return new MenuBar([
			new MenuBarItem("_File", [
				new MenuItem { Title = "_Open Project...", HelpText = "Ctrl+O", Key = Key.O.WithCtrl, Action = OpenProject },
				null!, // Separator
				new MenuItem { Title = "_Exit", HelpText = "Ctrl+Q", Key = Key.Q.WithCtrl, Command = Command.Quit }
			]),
			new MenuBarItem("_Tools", [
				new MenuItem { Title = "_Start Summarization", HelpText = "Ctrl+S", Key = Key.S.WithCtrl, Action = StartSummarization },
				new MenuItem { Title = "_Refresh Symbols", HelpText     = "Ctrl+R", Key = Key.R.WithCtrl, Action = RefreshSymbols },
				null!, // Separator
				new MenuItem { Title = "_Clear Cache", HelpText = "", Action = ClearCache }
			]),
			new MenuBarItem("_Help", [
				new MenuItem { Title = "_About", HelpText = "", Action = ShowAbout }
			])
		]);
	}

	private void OnFileSelected(string? filePath) {
		if (filePath == null) return;

		Task.Run(async () => {
			try {
				string? language = LangUtil.DetectLanguage(filePath);
				if (language != null) {
					var codeMap = await _crawler.CrawlFile(filePath);
					Application.Invoke(() => _symbolList.UpdateSymbols(codeMap.ToList()));
				}
			} catch (Exception ex) {
				_logger.LogError(ex, "Error loading symbols for file {FilePath}", filePath);
				Application.Invoke(() => SetStatusText($"Error: {ex.Message}"));
			}
		});
	}

	private void OnSymbolSelectionChanged(CodeSymbol? symbol) {
		if (symbol == null) {
			_summaryView.ClearSummary();
			return;
		}

		_summaryView.UpdateSummary(symbol);
	}

	private void OpenProject() {
		OpenDialog dialog = new OpenDialog {
			Title                   = "Open Project",
			OpenMode                = OpenMode.Directory,
			AllowsMultipleSelection = false
		};

		Application.Run(dialog);

		if (dialog.Canceled || !dialog.FilePaths.Any())
			return;

		string? projectPath = dialog.FilePaths.First();
		LoadProject(projectPath);
	}

	private void LoadProject(string projectPath) {
		Task.Run(async () => {
			try {
				Application.Invoke(() => SetStatusText("Loading project..."));

				_currentProjectPath = projectPath;

				// Detect primary language
				string? language = LangUtil.DetectPrimaryLanguage(projectPath);
				if (language == null) {
					Application.Invoke(() => SetStatusText("No supported language detected"));
					return;
				}

				// Load project structure
				List<string> projectFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
					.Where(LangUtil.IsSourceFile)
					.ToList();

				Application.Invoke(() => {
					_projectView.LoadFiles(projectPath);
					SetStatusText($"Loaded {projectFiles.Count} files from {Path.GetFileName(projectPath)}");
				});
			} catch (Exception ex) {
				_logger.LogError(ex, "Error loading project {ProjectPath}", projectPath);
				Application.Invoke(() => SetStatusText($"Error: {ex.Message}"));
			}
		});
	}

	private void StartSummarization() {
		if (_currentProjectPath == null) {
			SetStatusText("No project loaded");
			return;
		}

		Task.Run(async () => {
			try {
				Application.Invoke(() => {
					_progressView.Start("Summarizing codebase...");
					_progressView.Visible = true;
				});

				string? language = LangUtil.DetectPrimaryLanguage(_currentProjectPath);
				if (language == null) {
					Application.Invoke(() => SetStatusText("No supported language detected"));
					return;
				}

				SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(_currentProjectPath, language);
				_currentHierarchy = hierarchy;

				Application.Invoke(() => {
					_progressView.Visible = false;
					_symbolList.UpdateHierarchy(hierarchy);
					SetStatusText($"Summarization completed - {hierarchy.RootSymbols.Count} root symbols");
				});
			} catch (Exception ex) {
				_logger.LogError(ex, "Error during summarization");
				Application.Invoke(() => {
					_progressView.Visible = false;
					SetStatusText($"Summarization error: {ex.Message}");
				});
			}
		});
	}

	private void RefreshSymbols() {
		if (_currentProjectPath == null) return;

		Task.Run(async () => {
			try {
				string? language = LangUtil.DetectPrimaryLanguage(_currentProjectPath);
				if (language != null) {
					var codeMap = await _crawler.CrawlDir(_currentProjectPath);
					Application.Invoke(() => {
						_symbolList.UpdateSymbols(codeMap.ToList());
						SetStatusText($"Refreshed {codeMap.Count} symbols");
					});
				}
			} catch (Exception ex) {
				_logger.LogError(ex, "Error refreshing symbols");
				Application.Invoke(() => SetStatusText($"Refresh error: {ex.Message}"));
			}
		});
	}

	private void ClearCache() {
		// TODO: Implement cache clearing
		SetStatusText("Cache cleared");
	}

	private void ShowAbout() {
		MessageBox.Query("About Thaum",
			"Thaum v1.0\n\n" +
			"LSP-Based Codebase Summarization Tool\n\n" +
			"Built with .NET and Terminal.Gui\n" +
			"Supports multiple programming languages via LSP",
			"OK");
	}

	private void SetStatusText(string text) {
		// Update the first status item with the message
		if (_statusBar.SubViews.Count > 0 && _statusBar.SubViews.ElementAt(0) is Shortcut firstShortcut) {
			firstShortcut.Title = text;
		}
	}
}