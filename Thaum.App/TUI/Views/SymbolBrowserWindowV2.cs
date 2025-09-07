using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using Terminal.Gui;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Thaum.CLI.Models;
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
    private readonly Crawler _crawler;
    private readonly Compressor _compressor;
    private readonly ILogger<SymbolBrowserWindowV2> _log;
    private readonly string _projectPath;

    private TreeView<SymbolTreeNode> _treeView;
    private StatusBar _statusBar;
    private CodeMap _codeMap;

    public SymbolBrowserWindowV2(
        Crawler crawler,
        Compressor compressor,
        CodeMap codeMap,
        string projectPath)
    {
        _crawler = crawler;
        _compressor = compressor;
        _log = Logging.For<SymbolBrowserWindowV2>();
        _projectPath = projectPath;
        _codeMap = codeMap;

        InitializeWindow();
        LoadSymbols();
        SetupCommands();
    }

    private void InitializeWindow()
    {
        // Use Window instead of custom View - provides proper borders, title, etc.
        Title = $"Thaum Symbol Browser - {Path.GetFileName(_projectPath)}";

        // Let Terminal.Gui v2 handle color schemes automatically
        // No manual ColorScheme setting needed

        // Use proper v2 TreeView instead of custom ListView wrapper
        _treeView = new TreeView<SymbolTreeNode>
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1), // Reserve space for status bar
            CanFocus = true,
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
        _treeView.ObjectActivated += OnObjectActivated;
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
        KeyBindings.Add(Key.F1, Command.Help);
        AddCommand(Command.Help, () => {
            ShowHelp();
            return true;
        });

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

    private void ShowPromptSelector(CodeSymbol symbol)
    {
        var dialog = new Dialog
        {
            Title = "Select Compression Prompt",
            Width = Dim.Percent(60),
            Height = Dim.Percent(40)
        };

        var promptList = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        // Add available prompts (simplified for demo)
        var prompts = new[] { "compress_function_v5", "fusion_v1", "grow_v2", "infuse_v2" };
        promptList.SetSource(prompts);

        var okButton = new Button { Text = "OK", IsDefault = true };
        var cancelButton = new Button { Text = "Cancel" };

        okButton.Accept += (s, e) => {
            if (promptList.SelectedItem >= 0)
            {
                var selectedPrompt = prompts[promptList.SelectedItem];
                ExecuteCompression(symbol, selectedPrompt);
            }
            dialog.RequestStop();
        };

        cancelButton.Accept += (s, e) => dialog.RequestStop();

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