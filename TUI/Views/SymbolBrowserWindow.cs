using Microsoft.Extensions.Logging;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using TreeNode = Thaum.CLI.Models.TreeNode; // Resolve naming conflict
using Thaum.CLI.Models;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Core.Utils;
using Thaum.TUI.Models;
using Thaum.Utils;

namespace Thaum.TUI.Views;

/// <summary>
/// Main interactive symbol browser providing keyboard navigation through codebase symbols
/// in a borderless terminal-native interface that respects user's color scheme and provides
/// airline-style status information with smooth navigation and auto-scroll capabilities
/// </summary>
public class SymbolBrowserWindow : View {
	private readonly Crawler _crawler;
	private readonly Compressor _compressor;
	private readonly ILogger _logger;
	private readonly BrowserState _state;
	
	private Label _titleBar;
	private SymbolTreeView _symbolTree;
	private StatusBarView _statusBar;
	private readonly PerceptualColorer _colorer;
	
	public SymbolBrowserWindow(
		Crawler crawler,
		Compressor compressor,
		ILogger logger,
		CodeMap codeMap,
		string projectPath) {
		
		_crawler = crawler;
		_compressor = compressor;
		_logger = logger;
		_colorer = new PerceptualColorer();
		
		_state = new BrowserState {
			ProjectPath = projectPath,
			CodeMap = codeMap,
			Language = "auto",
			CompactMode = true
		};
		
		InitializeComponents();
		LoadSymbols();
		SetupKeyBindings();
		SetupCommands();
		SetupResizeHandling();
	}
	
	private void InitializeComponents() {
		Width = Dim.Fill();
		Height = Dim.Fill();
		
		// Use terminal's native color scheme (v2: Set automatically)
		
		// Airline-style title bar at top
		_titleBar = new Label {
			X = 0,
			Y = 0,
			Width = Dim.Fill(),
			Height = 1
		};
		
		// Symbol tree view (main content, reserve space for title and status)
		_symbolTree = new SymbolTreeView(_state) {
			X = 0,
			Y = 1, // Below title bar
			Width = Dim.Fill(),
			Height = Dim.Fill(2) // Reserve space for title and status bar
		};
		
		// Status bar at bottom
		_statusBar = new StatusBarView(_state) {
			X = 0,
			Y = Pos.Bottom(_symbolTree),
			Width = Dim.Fill(),
			Height = 1
		};
		
		Add(_titleBar, _symbolTree, _statusBar);
		UpdateTitleBar();
	}
	
	private void LoadSymbols() {
		// Convert CodeMap to TreeNode hierarchy using existing logic
		var symbols = _state.CodeMap.ToList();
		_state.DisplayNodes = TreeNode.BuildHierarchy(symbols, _colorer);
		
		_symbolTree.UpdateNodes(_state.DisplayNodes);
		_statusBar.UpdateDisplay();
		
		_logger.LogInformation("Loaded {Count} files with {SymbolCount} symbols", 
			_state.CodeMap.FileCount, _state.CodeMap.Count);
		
		UpdateTitleBar();
	}
	
	private void UpdateTitleBar() {
		var projectName = Path.GetFileName(_state.ProjectPath) ?? _state.ProjectPath;
		var fileCount = _state.CodeMap.FileCount;
		var symbolCount = _state.CodeMap.Count;
		var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), _state.ProjectPath);
		
		_titleBar.Text = $"Thaum Interactive Browser | {fileCount} files | {symbolCount} symbols | ~/{relativePath}";
	}
	
	private void SetupKeyBindings() {
		CanFocus = true;
	}
	
	// v2 API: Use Command system for key handling
	private void SetupCommands() {
		// Navigation commands
		AddCommand(Command.Up, ctx => { Navigate(-1); return true; });
		AddCommand(Command.Down, ctx => { Navigate(1); return true; });
		
		// Selection command
		AddCommand(Command.Accept, ctx => { ShowPromptSelector(); return true; });
		
		// Toggle view mode
		AddCommand(Command.Toggle, ctx => { ToggleViewMode(); return true; });
		
		// Refresh
		AddCommand(Command.Refresh, ctx => { RefreshSymbols(); return true; });
		
		// Setup key bindings
		KeyBindings.Add(Key.CursorUp, Command.Up);
		KeyBindings.Add(Key.CursorDown, Command.Down);
		// Enter -> Command.Accept binding already exists by default in Terminal.Gui v2
		KeyBindings.Add(Key.Tab, Command.Toggle);
		KeyBindings.Add(Key.V, Command.Toggle);
		KeyBindings.Add(Key.V.WithShift, Command.Toggle);
		KeyBindings.Add(Key.R, Command.Refresh);
		KeyBindings.Add(Key.R.WithShift, Command.Refresh);
		KeyBindings.Add(Key.Esc, Command.Quit);
		KeyBindings.Add(Key.Q, Command.Quit);
		KeyBindings.Add(Key.Q.WithShift, Command.Quit);
		KeyBindings.Add(Key.C.WithCtrl, Command.Quit);
	}
	
	private bool Navigate(int direction) {
		int newIndex = _state.SelectedIndex + direction;
		if (newIndex >= 0 && newIndex < _state.DisplayNodes.Count) {
			_state.SelectedIndex = newIndex;
			_symbolTree.SetSelection(_state.SelectedIndex);
			_symbolTree.EnsureSelectionVisible(); // Auto-scroll to keep selection visible
			_statusBar.UpdateDisplay();
			return true;
		}
		return false;
	}
	
	private void SetupResizeHandling() {
		// v2: Resize handling is automatic
	}
	
	private void OnApplicationResized(object sender, EventArgs e) {
		// v2: Resize handling is automatic
		SetNeedsDraw();
		UpdateTitleBar();
	}
	
	private bool ToggleViewMode() {
		_state.CompactMode = !_state.CompactMode;
		_symbolTree.SetCompactMode(_state.CompactMode);
		_statusBar.UpdateDisplay();
		return true;
	}
	
	private bool ShowPromptSelector() {
		if (_state.SelectedNode?.Symbol == null) {
			return false;
		}
		
		var promptDialog = new PromptSelectorDialog(_state.SelectedNode.Symbol);
		var selectedPrompt = promptDialog.ShowDialog();
		
		if (!string.IsNullOrEmpty(selectedPrompt)) {
			_state.CurrentView = ViewMode.Compress;
			_statusBar.UpdateDisplay();
			
			// TODO: Execute compression with selected prompt
			ExecuteCompression(_state.SelectedNode.Symbol, selectedPrompt);
			
			_state.CurrentView = ViewMode.Map;
			_statusBar.UpdateDisplay();
		}
		
		return true;
	}
	
	private async void ExecuteCompression(CodeSymbol symbol, string promptName) {
		try {
			_logger.LogInformation("Executing compression for symbol {Symbol} with prompt {Prompt}", 
				symbol.Name, promptName);
			
			// This would integrate with the existing Compressor service
			// For now, just show a placeholder
			MessageBox.Query("Compression", $"Would compress {symbol.Name} with {promptName}", "OK");
			
		} catch (Exception ex) {
			_logger.LogError(ex, "Error during compression");
			MessageBox.ErrorQuery("Error", $"Compression failed: {ex.Message}", "OK");
		}
	}
	
	private async void RefreshSymbols() {
		try {
			var codeMap = await _crawler.CrawlDir(_state.ProjectPath);
			_state.CodeMap = codeMap;
			LoadSymbols();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error refreshing symbols");
			MessageBox.ErrorQuery("Error", $"Failed to refresh: {ex.Message}", "OK");
		}
	}
}