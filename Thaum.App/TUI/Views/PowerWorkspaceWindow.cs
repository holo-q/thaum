using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Terminal.Gui;
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
/// Power-user workspace for Thaum: left symbols tree, right detail tabs, top search, bottom status.
/// Built on v2 TreeView + TabView + StatusBar.
/// </summary>
public class PowerWorkspaceWindow : Window
{
    private readonly Crawler    _crawler;
    private readonly Compressor _compressor;
    private readonly ILogger    _log;

    private readonly string _projectPath;
    private CodeMap         _codeMap;

    // UI
    private TextField                   _searchBox = null!;
    private TreeView<SymbolTreeNode>    _tree      = null!;
    private TabView                     _tabs      = null!;
    private StatusBar                   _status    = null!;

    private ObservableCollection<string> _logLines = new();

    public PowerWorkspaceWindow(Crawler crawler, Compressor compressor, CodeMap codeMap, string projectPath)
    {
        _crawler     = crawler;
        _compressor  = compressor;
        _log         = Logging.For<PowerWorkspaceWindow>();
        _codeMap     = codeMap;
        _projectPath = projectPath;

        Title  = $"Thaum Power Workspace - {Path.GetFileName(projectPath)}";
        Width  = Dim.Fill();
        Height = Dim.Fill();

        BuildLayout();
        LoadSymbols(_codeMap);
        SetupCommands();
    }

    private void BuildLayout()
    {
        // Top search line
        var searchLabel = new Label { X = 0, Y = 0, Text = "Search:" };
        _searchBox = new TextField { X = Pos.Right(searchLabel) + 1, Y = 0, Width = Dim.Fill() };
        _searchBox.TextChanging += (s, e) => ApplyFilter(e.NewValue?.ToString() ?? string.Empty);

        // Left tree (fills height minus status bar)
        _tree = new TreeView<SymbolTreeNode>
        {
            X           = 0,
            Y           = 1,
            Width       = Dim.Percent(40),
            Height      = Dim.Fill(1),
            CanFocus    = true,
            TreeBuilder = new SymbolTreeBuilder()
        };

        _tree.SelectionChanged += (s, e) => UpdateDetail(e.NewValue);
        _tree.ObjectActivated  += (s, e) => OnTreeActivated(e.Object);

        // Right tabs
        _tabs = new TabView
        {
            X      = Pos.Right(_tree),
            Y      = 1,
            Width  = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        _tabs.AddTab(new TabView.Tab("Summary", new View { Width = Dim.Fill(), Height = Dim.Fill() }), false);
        _tabs.AddTab(new TabView.Tab("Triad", new View { Width = Dim.Fill(), Height = Dim.Fill() }), false);
        _tabs.AddTab(new TabView.Tab("Diff", new View { Width = Dim.Fill(), Height = Dim.Fill() }), false);

        var logView = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        logView.SetSource(_logLines);
        _tabs.AddTab(new TabView.Tab("Logs", logView), true);

        // Bottom status with quick actions
        _status = new StatusBar([
            new Shortcut(Key.F1, "Help", ShowHelp),
            new Shortcut(Key.F5, "Refresh", RefreshProject),
            new Shortcut(Key.F9, "Schemes", CycleScheme),
            new Shortcut(Key.F10, "16c/24b", ToggleColors),
            new Shortcut(Key.Esc, "Exit", () => RequestStop())
        ]);

        Add(searchLabel, _searchBox, _tree, _tabs, _status);
    }

    private void LoadSymbols(CodeMap map)
    {
        var nodes = SymbolTreeNode.BuildFromCodeMap(map);
        _tree.ClearObjects();
        foreach (var n in nodes) _tree.AddObject(n);
        _tree.ExpandAll();
    }

    private void SetupCommands()
    {
        KeyBindings.Add(Key.Tab, Command.Toggle);
        AddCommand(Command.Toggle, () => { ToggleExpandCollapse(); return true; });

        KeyBindings.Add(Key.F9, Command.Edit);
        AddCommand(Command.Edit, () => { CycleScheme(); return true; });

        KeyBindings.Add(Key.F10, Command.Save);
        AddCommand(Command.Save, () => { ToggleColors(); return true; });
    }

    private void ToggleExpandCollapse()
    {
        // Simple heuristic: collapse if most expanded
        _tree.CollapseAll();
        _tree.SetNeedsDraw();
    }

    private void UpdateDetail(SymbolTreeNode? selected)
    {
        if (selected == null)
        {
            Title = $"Thaum Power Workspace - {Path.GetFileName(_projectPath)}";
            return;
        }
        Title = selected.IsFile
            ? $"{Path.GetFileName(selected.FilePath)}"
            : $"{selected.Symbol!.Name} ({selected.Symbol.Kind})";
    }

    private void OnTreeActivated(SymbolTreeNode obj)
    {
        // Stub: Future hook to open prompt selector or show triad
        _logLines.Add($"Activate: {(obj.IsFile ? obj.FilePath : obj.Symbol!.Name)}");
        _tabs.SelectedTab = _tabs.Tabs.FirstOrDefault(t => t.Text == "Triad") ?? _tabs.SelectedTab;
        _tabs.SetNeedsDraw();
    }

    private void ApplyFilter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            LoadSymbols(_codeMap);
            return;
        }

        // naive filter: include files/symbols whose name contains the text
        var filtered = new CodeMap();
        foreach (var sym in _codeMap)
        {
            if (sym.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(sym.FilePath).Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                filtered.Add(sym);
            }
        }
        LoadSymbols(filtered);
    }

    private void ShowHelp()
    {
        var help = @"Thaum Power Workspace\n\n"
                   + "Search: type to filter files/symbols\n"
                   + "Enter: open triad tab (stub)\n"
                   + "Tab: collapse\n"
                   + "F5: refresh project\n"
                   + "F9: cycle schemes\n"
                   + "F10: toggle 16-colors vs TrueColor\n";
        MessageBox.Query("Help", help, "OK");
    }

    private async void RefreshProject()
    {
        try
        {
            var language = LangUtil.DetectPrimaryLanguage(_projectPath);
            if (language is null)
            {
                MessageBox.ErrorQuery("Refresh", "No supported language detected", "OK");
                return;
            }
            _codeMap = await _crawler.CrawlDir(_projectPath);
            LoadSymbols(_codeMap);
            _logLines.Add($"Refreshed: {_codeMap.Count} symbols");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Refresh error");
            MessageBox.ErrorQuery("Refresh", ex.Message, "OK");
        }
    }

    private void CycleScheme()
    {
        // Defer to SymbolBrowserWindowV2 implementation via F9 handling
        Application.Raise(Command.Edit);
    }

    private void ToggleColors()
    {
        Application.Force16Colors = !Application.Force16Colors;
        Application.Driver?.ClearContents();
        SetNeedsDraw();
    }
}

