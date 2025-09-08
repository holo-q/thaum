using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Terminal.Gui;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.TUI.Models;
using Thaum.Utils;
using Logging = Thaum.Utils.Logging;

namespace Thaum.TUI.Views;

/// <summary>
/// Modern Terminal.Gui v2 symbol browser using proper TreeView, built-in scrolling,
/// and v2 command system where the interface leverages native v2 capabilities
/// for clean hierarchical symbol navigation with automatic theme support
/// </summary>
public class SymbolBrowserWindowV2 : Window
{
	private readonly Crawler                        _crawler;
	private readonly Compressor                     _compressor;
	private readonly ILogger<SymbolBrowserWindowV2> _log;
	private readonly string                         _projectPath;

    private TreeView<SymbolTreeNode> _treeView;
    private StatusBar                _statusBar;
    private CodeMap                  _codeMap;

    private bool _expanded = true; // expanded view vs. collapsed (compact)
    private readonly List<string> _availableSchemes = new();
    private int _schemeIndex = 0;

	public SymbolBrowserWindowV2(
		Crawler    crawler,
		Compressor compressor,
		CodeMap    codeMap,
		string     projectPath)
	{
		_crawler     = crawler;
		_compressor  = compressor;
		_log         = Logging.For<SymbolBrowserWindowV2>();
		_projectPath = projectPath;
		_codeMap     = codeMap;

        InitializeWindow();
        LoadSymbols();
        SetupCommands();
    }

	private void InitializeWindow()
	{
		// Use Window instead of custom View - provides proper borders, title, etc.
		Title = $"Thaum Symbol Browser - {Path.GetFileName(_projectPath)}";

        // Setup schemes: add a dark scheme that better matches typical dark terminals
        SetupColorSchemes();

		// Use proper v2 TreeView instead of custom ListView wrapper
		_treeView = new TreeView<SymbolTreeNode>
		{
			X           = 0,
			Y           = 0,
			Width       = Dim.Fill(),
			Height      = Dim.Fill(1), // Reserve space for status bar
			CanFocus    = true,
			TreeBuilder = new SymbolTreeBuilder()
		};

		// Use proper v2 StatusBar
		_statusBar = new StatusBar([
			new Shortcut(Key.F1, "Help", () => ShowHelp()),
			new Shortcut(Key.F5, "Refresh", () => RefreshSymbols()),
			new Shortcut(Key.Enter, "Select", () => HandleAccept()),
			new Shortcut(Key.Esc, "Exit", () => RequestStop())
		]);

        Add(_treeView, _statusBar);

        // Hook into TreeView events
        _treeView.SelectionChanged += OnSelectionChanged;
        _treeView.ObjectActivated  += OnObjectActivated;

        // Start expanded for better initial discoverability
        _treeView.ExpandAll();
    }

	private void LoadSymbols()
	{
		var nodes = SymbolTreeNode.BuildFromCodeMap(_codeMap);

		_treeView.ClearObjects();
		foreach (var node in nodes)
		{
			_treeView.AddObject(node);
		}

		_treeView.SetNeedsDraw();

		_log.LogInformation("Loaded {FileCount} files with {SymbolCount} symbols",
			_codeMap.FileCount,
			_codeMap.Count);
	}

    private void SetupCommands()
    {
		// v2 uses proper Command system instead of manual key bindings

		// F5 for refresh
		KeyBindings.Add(Key.F5, Command.Refresh);
		AddCommand(Command.Refresh, () => {
			RefreshSymbols();
			return true;
		});

        // F1 for help
		// KeyBindings.Add(Key.F1, Command.Help);
		// AddCommand(Command.Help, () => {
		//     ShowHelp();
		//     return true;
		// });

		// Enter to select/execute
		KeyBindings.Add(Key.Enter, Command.Accept);
		AddCommand(Command.Accept, () => {
			HandleAccept();
			return true;
		});

        // Escape to quit
        KeyBindings.Add(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => {
            RequestStop();
            return true;
        });

        // Tab to toggle expand/collapse (compact vs expanded)
        KeyBindings.Add(Key.Tab, Command.Toggle);
        AddCommand(Command.Toggle, () => {
            ToggleExpandCollapse();
            return true;
        });

        // F9 to cycle color schemes (helps find one that matches terminal)
        KeyBindings.Add(Key.F9, Command.Edit);
        AddCommand(Command.Edit, () => {
            CycleScheme();
            return true;
        });

        // F10 to toggle 16-colors vs TrueColor at runtime
        KeyBindings.Add(Key.F10, Command.Save);
        AddCommand(Command.Save, () => {
            Application.Force16Colors = !Application.Force16Colors;
            // Trigger redraws so the new mode is visible immediately
            Application.Driver?.ClearContents();
            _treeView.SetNeedsDraw();
            _statusBar.SetNeedsDraw();
            SetNeedsDraw();
            return true;
        });
    }

	private void OnSelectionChanged(object? sender, SelectionChangedEventArgs<SymbolTreeNode> e)
	{
		// Update status based on selection
		var selected = e.NewValue;
		if (selected == null)
		{
			Title = $"Thaum Symbol Browser - {Path.GetFileName(_projectPath)}";
		}
		else if (selected.IsFile)
		{
			Title = $"Thaum Symbol Browser - {Path.GetFileName(selected.FilePath)}";
		}
		else
		{
			Title = $"Thaum Symbol Browser - {selected.Symbol!.Name} ({selected.Symbol.Kind})";
		}

		SetNeedsDraw();
	}

	private void OnObjectActivated(object? sender, ObjectActivatedEventArgs<SymbolTreeNode> e)
	{
		HandleAccept();
	}

    private void HandleAccept()
    {
        var selected = _treeView.SelectedObject;
        if (selected?.Symbol != null)
        {
            ShowPromptSelector(selected.Symbol);
        }
    }

    private void ToggleExpandCollapse()
    {
        _expanded = !_expanded;
        if (_expanded)
        {
            _treeView.ExpandAll();
            _statusBar.SetNeedsDraw();
            Title = $"Thaum Symbol Browser - Expanded";
        }
        else
        {
            _treeView.CollapseAll();
            _statusBar.SetNeedsDraw();
            Title = $"Thaum Symbol Browser - Collapsed";
        }
        SetNeedsDraw();
    }

    private void SetupColorSchemes()
    {
        try
        {
            // Create a scheme with black background and light foreground for dark terminals
            var dark = new Scheme(new Terminal.Gui.Drawing.Attribute(StandardColor.LightGray, StandardColor.Black));
            // Register if not present
            var names = SchemeManager.GetSchemeNames();
            if (!names.Contains("ThaumDark"))
            {
                SchemeManager.AddScheme("ThaumDark", dark);
            }

            // Build a cycle list of candidate schemes to try
            _availableSchemes.Clear();
            _availableSchemes.Add("ThaumDark");
            var baseName = SchemeManager.SchemesToSchemeName(Schemes.Base);
            var topName  = SchemeManager.SchemesToSchemeName(Schemes.Toplevel);
            var dlgName  = SchemeManager.SchemesToSchemeName(Schemes.Dialog);
            if (baseName is { }) _availableSchemes.Add(baseName);
            if (topName  is { }) _availableSchemes.Add(topName);
            if (dlgName  is { }) _availableSchemes.Add(dlgName);

            // Apply our preferred default scheme
            ApplyScheme(_availableSchemes[0]);
        }
        catch
        {
            // Fall back silently if configuration subsystem is not ready; default theme will apply
        }
    }

    private void CycleScheme()
    {
        if (_availableSchemes.Count == 0) return;
        _schemeIndex = (_schemeIndex + 1) % _availableSchemes.Count;
        ApplyScheme(_availableSchemes[_schemeIndex]);
        SetNeedsDraw();
    }

    private void ApplyScheme(string schemeName)
    {
        try
        {
            var scheme = SchemeManager.GetScheme(schemeName);
            // Set on the window so children inherit unless they override
            SetScheme(scheme);
            // Ensure status bar and tree view match
            _statusBar?.SetScheme(scheme);
            _treeView?.SetScheme(scheme);
        }
        catch
        {
            // Ignore if scheme not found; keep current
        }
    }

	private void ShowPromptSelector(CodeSymbol symbol)
	{
		var dialog = new Dialog
		{
			Title  = "Select Compression Prompt",
			Width  = Dim.Percent(60),
			Height = Dim.Percent(40)
		};

		var promptList = new ListView
		{
			X      = 0,
			Y      = 0,
			Width  = Dim.Fill(),
			Height = Dim.Fill(1)
		};

		// Add available prompts (simplified for demo)
		var prompts = new ObservableCollection<string> { "compress_function_v5", "fusion_v1", "grow_v2", "infuse_v2" };
		promptList.SetSource(prompts);

		var okButton     = new Button { Text = "OK", IsDefault = true };
		var cancelButton = new Button { Text = "Cancel" };

		okButton.Accepting += (s, e) => {
			if (promptList.SelectedItem >= 0)
			{
				var selectedPrompt = prompts[promptList.SelectedItem];
				ExecuteCompression(symbol, selectedPrompt);
			}
			dialog.RequestStop();
		};

		cancelButton.Accepting += (s, e) => dialog.RequestStop();

		dialog.Add(promptList);
		dialog.AddButton(okButton);
		dialog.AddButton(cancelButton);

		Application.Run(dialog);
	}

	private async void ExecuteCompression(CodeSymbol symbol, string promptName)
	{
		try
		{
			_log.LogInformation("Executing compression for symbol {Symbol} with prompt {Prompt}",
				symbol.Name, promptName);

			// Show a simple message for now
			var result = MessageBox.Query("Compression",
				$"Would compress {symbol.Name} with {promptName}",
				"OK");
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "Error during compression");
			MessageBox.ErrorQuery("Error", $"Compression failed: {ex.Message}", "OK");
		}
	}

	private async void RefreshSymbols()
	{
		try
		{
			_codeMap = await _crawler.CrawlDir(_projectPath);
			LoadSymbols();
		}
		catch (Exception ex)
		{
			_log.LogError(ex, "Error refreshing symbols");
			MessageBox.ErrorQuery("Error", $"Failed to refresh: {ex.Message}", "OK");
		}
	}

    private void ShowHelp()
    {
        var help = @"Thaum Symbol Browser Help

Navigation:
  ↑/↓         Navigate symbols
  →/←         Expand/collapse files
  Enter       Select symbol for compression
  F5          Refresh symbols
  F1          Show this help
  Esc         Exit browser

View:
  Tab         Toggle expand/collapse all
  F9          Cycle color schemes
  F10         Toggle 16-colors vs TrueColor

The browser shows code symbols organized by file.
Select a symbol and press Enter to choose a compression prompt.";

		MessageBox.Query("Help", help, "OK");
	}
}

/// <summary>
/// TreeBuilder implementation for SymbolTreeNode hierarchy
/// enabling proper Terminal.Gui v2 TreeView integration
/// </summary>
public class SymbolTreeBuilder : ITreeBuilder<SymbolTreeNode>
{
	public bool SupportsCanExpand => true;

	public bool CanExpand(SymbolTreeNode node)
	{
		return node.IsFile && node.Children.Any();
	}

	public IEnumerable<SymbolTreeNode> GetChildren(SymbolTreeNode node)
	{
		return node.Children.Cast<SymbolTreeNode>();
	}
}
