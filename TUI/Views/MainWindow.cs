using Microsoft.Extensions.Logging;
using Terminal.Gui;
using Thaum.Core.Models;
using Thaum.Core.Services;

namespace Thaum.UI.Views;

public class MainWindow : Window {
	private readonly CodeCrawler _codeCrawlerManager;
	private readonly Compressor  _compressor;
	private readonly ILogger     _logger;

	private readonly MenuBar           _menuBar;
	private readonly SimpleProjectView _projectView;
	private readonly SymbolListView    _symbolList;
	private readonly SummaryView       _summaryView;
	private readonly StatusBar         _statusBar;
	private readonly ProgressView      _progressView;

	private string?          _currentProjectPath;
	private SymbolHierarchy? _currentHierarchy;

	public MainWindow(
		CodeCrawler crawler,
		Compressor  compressor,
		ILogger     logger) : base("Thaum - LSP Codebase Summarizer") {
		_codeCrawlerManager = crawler;
		_compressor         = compressor;
		_logger             = logger;

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
			new(Key.CtrlMask | Key.O, "~Ctrl+O~ Open", OpenProject),
			new(Key.CtrlMask | Key.S, "~Ctrl+S~ Summarize", StartSummarization),
			new(Key.CtrlMask | Key.Q, "~Ctrl+Q~ Quit", () => Application.RequestStop())
		]);

		mainContainer.Add(_projectView, _symbolList, _summaryView);
		Add(_menuBar, mainContainer, _statusBar, _progressView);
	}

	private MenuBar CreateMenuBar() {
		return new MenuBar([
			new("_File", new MenuItem[] {
				new("_Open Project...", "Ctrl+O", OpenProject),
				null!, // Separator
				new("_Exit", "Ctrl+Q", () => Application.RequestStop())
			}),
			new("_Tools", new MenuItem[] {
				new("_Start Summarization", "Ctrl+S", StartSummarization),
				new("_Refresh Symbols", "Ctrl+R", RefreshSymbols),
				null!,
				new("_Clear Cache", "", ClearCache)
			}),
			new("_Help", new MenuItem[] {
				new("_About", "", ShowAbout)
			})
		]);
	}

	private void OnFileSelected(string? filePath) {
		if (filePath == null) return;

		Task.Run(async () => {
			try {
				string? language = DetectLanguage(filePath);
				if (language != null) {
					List<CodeSymbol> symbols = await _codeCrawlerManager.CrawlFile(filePath);
					Application.MainLoop.Invoke(() => _symbolList.UpdateSymbols(symbols));
				}
			} catch (Exception ex) {
				_logger.LogError(ex, "Error loading symbols for file {FilePath}", filePath);
				Application.MainLoop.Invoke(() => SetStatusText($"Error: {ex.Message}"));
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
		OpenDialog dialog = new OpenDialog("Open Project", "Select project folder") {
			CanChooseDirectories    = true,
			CanChooseFiles          = false,
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
				Application.MainLoop.Invoke(() => SetStatusText("Loading project..."));

				_currentProjectPath = projectPath;

				// Detect primary language
				string? language = DetectPrimaryLanguage(projectPath);
				if (language == null) {
					Application.MainLoop.Invoke(() => SetStatusText("No supported language detected"));
					return;
				}

				// Load project structure
				List<string> projectFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
					.Where(IsSourceFile)
					.ToList();

				Application.MainLoop.Invoke(() => {
					_projectView.LoadFiles(projectPath);
					SetStatusText($"Loaded {projectFiles.Count} files from {Path.GetFileName(projectPath)}");
				});
			} catch (Exception ex) {
				_logger.LogError(ex, "Error loading project {ProjectPath}", projectPath);
				Application.MainLoop.Invoke(() => SetStatusText($"Error: {ex.Message}"));
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
				Application.MainLoop.Invoke(() => {
					_progressView.Start("Summarizing codebase...");
					_progressView.Visible = true;
				});

				string? language = DetectPrimaryLanguage(_currentProjectPath);
				if (language == null) {
					Application.MainLoop.Invoke(() => SetStatusText("No supported language detected"));
					return;
				}

				SymbolHierarchy hierarchy = await _compressor.ProcessCodebaseAsync(_currentProjectPath, language);
				_currentHierarchy = hierarchy;

				Application.MainLoop.Invoke(() => {
					_progressView.Visible = false;
					_symbolList.UpdateHierarchy(hierarchy);
					SetStatusText($"Summarization completed - {hierarchy.RootSymbols.Count} root symbols");
				});
			} catch (Exception ex) {
				_logger.LogError(ex, "Error during summarization");
				Application.MainLoop.Invoke(() => {
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
				string? language = DetectPrimaryLanguage(_currentProjectPath);
				if (language != null) {
					List<CodeSymbol> symbols = await _codeCrawlerManager.CrawlDir(_currentProjectPath);
					Application.MainLoop.Invoke(() => {
						_symbolList.UpdateSymbols(symbols);
						SetStatusText($"Refreshed {symbols.Count} symbols");
					});
				}
			} catch (Exception ex) {
				_logger.LogError(ex, "Error refreshing symbols");
				Application.MainLoop.Invoke(() => SetStatusText($"Refresh error: {ex.Message}"));
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
		_statusBar.Items[0] = new StatusItem(Key.Null, text, null);
	}

	private static string? DetectLanguage(string filePath) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		return extension switch {
			".py"                     => "python",
			".cs"                     => "c-sharp",
			".js"                     => "javascript",
			".ts"                     => "typescript",
			".rs"                     => "rust",
			".go"                     => "go",
			".java"                   => "java",
			".cpp" or ".cc" or ".cxx" => "cpp",
			".c"                      => "c",
			_                         => null
		};
	}

	private static string? DetectPrimaryLanguage(string projectPath) {
		string[] files = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories);
		Dictionary<string, int> extensionCounts = files
			.Select(Path.GetExtension)
			.Where(ext => !string.IsNullOrEmpty(ext))
			.GroupBy(ext => ext.ToLowerInvariant())
			.ToDictionary(g => g.Key, g => g.Count());

		Dictionary<string, string> languageMap = new Dictionary<string, string> {
			[".py"] = "python",
			[".cs"] = "c-sharp",
			[".js"] = "javascript",
			[".ts"] = "typescript",
			[".rs"] = "rust",
			[".go"] = "go"
		};

		KeyValuePair<string, int> primaryExtension = extensionCounts
			.Where(kv => languageMap.ContainsKey(kv.Key))
			.OrderByDescending(kv => kv.Value)
			.FirstOrDefault();

		return primaryExtension.Key != null ? languageMap[primaryExtension.Key] : null;
	}

	private static bool IsSourceFile(string filePath) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		return extension is ".py" or ".cs" or ".js" or ".ts" or ".rs" or ".go" or ".java" or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp";
	}
}