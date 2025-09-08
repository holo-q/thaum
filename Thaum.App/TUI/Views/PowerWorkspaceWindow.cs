using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
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
    private View                        _summaryTabView = null!;
    private TextView                    _summaryText    = null!;
    private View                        _diffTabView    = null!;
    private TextView                    _diffLeft       = null!;
    private TextView                    _diffRight      = null!;
    private ListView                    _jobsList       = null!;
    private readonly ObservableCollection<JobItem> _jobs = new();

    private ObservableCollection<string> _logLines = new();
    private readonly List<string> _availableSchemes = new();
    private int _schemeIndex = 0;

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
        SetupColorSchemes();
    }

    private void BuildLayout()
    {
        // Top search line
        var searchLabel = new Label { X = 0, Y = 0, Text = "Search:" };
        _searchBox = new TextField { X = Pos.Right(searchLabel) + 1, Y = 0, Width = Dim.Fill() };
        _searchBox.TextChanged += (s, e) => ApplyFilter(_searchBox.Text?.ToString() ?? string.Empty);

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
        _tree.ObjectActivated  += (s, e) => OnTreeActivated(e.ActivatedObject);

        // Right tabs
        _tabs = new TabView
        {
            X      = Pos.Right(_tree),
            Y      = 1,
            Width  = Dim.Fill(),
            Height = Dim.Fill(1)
        };

        // Summary tab
        _summaryTabView = new View { Width = Dim.Fill(), Height = Dim.Fill() };
        _summaryText    = new TextView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true };
        _summaryTabView.Add(_summaryText);
        var tabSummary = new Tab { DisplayText = "Summary", View = _summaryTabView };
        _tabs.AddTab(tabSummary, false);

        // Triad tab placeholder (wired later)
        var tabTriad = new Tab { DisplayText = "Triad", View = new View { Width = Dim.Fill(), Height = Dim.Fill() } };
        _tabs.AddTab(tabTriad, false);

        // Diff tab side-by-side
        _diffTabView = new View { Width = Dim.Fill(), Height = Dim.Fill() };
        _diffLeft    = new TextView { X = 0, Y = 0, Width = Dim.Percent(50), Height = Dim.Fill(), ReadOnly = true };
        _diffRight   = new TextView { X = Pos.Right(_diffLeft), Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), ReadOnly = true };
        _diffTabView.Add(_diffLeft, _diffRight);
        var tabDiff = new Tab { DisplayText = "Diff", View = _diffTabView };
        _tabs.AddTab(tabDiff, false);

        var logView = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        logView.SetSource(_logLines);
        var tabLogs = new Tab { DisplayText = "Logs", View = logView };
        _tabs.AddTab(tabLogs, true);

        // Jobs tab
        _jobsList = new ListView { Width = Dim.Fill(), Height = Dim.Fill() };
        _jobsList.SetSource(new ObservableCollection<string>(_jobs.Select(j => j.ToString()).ToList()));
        var tabJobs = new Tab { DisplayText = "Jobs", View = _jobsList };
        _tabs.AddTab(tabJobs, false);

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

        // Enter to enqueue a Try job on selected symbol (simulated)
        KeyBindings.Add(Key.Enter, Command.Accept);
        AddCommand(Command.Accept, () =>
        {
            var sel = _tree.SelectedObject;
            if (sel?.IsFile == false && sel.Symbol is { } sym)
            {
                EnqueueJob(sym, promptName: "compress_function_v5");
                return true;
            }
            return false;
        });
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

        // Update Summary tab
        if (!selected.IsFile && selected.Symbol is { } sym)
        {
            _summaryText.Text = BuildSymbolSummary(sym);
            _tabs.SelectedTab = _tabs.Tabs.FirstOrDefault(t => t.DisplayText == "Summary") ?? _tabs.SelectedTab;
        }
        else if (selected.IsFile)
        {
            _summaryText.Text = BuildFileSummary(selected.FilePath);
        }
    }

    private void OnTreeActivated(SymbolTreeNode obj)
    {
        // Stub: Future hook to open prompt selector or show triad
        _logLines.Add($"Activate: {(obj.IsFile ? obj.FilePath : obj.Symbol!.Name)}");
        if (!obj.IsFile)
        {
            // Populate diff with original snippet and a placeholder rehydrated text
            var original = ReadSnippet(obj.FilePath, obj.Symbol!.StartCodeLoc.Line, obj.Symbol.EndCodeLoc.Line, 120);
            var placeholder = $"// Rehydrated placeholder for {obj.Symbol.Name}\n" + original;
            _diffLeft.Text  = original;
            _diffRight.Text = placeholder;
            _tabs.SelectedTab = _tabs.Tabs.FirstOrDefault(t => t.DisplayText == "Diff") ?? _tabs.SelectedTab;
        }
        _tabs.SetNeedsDraw();
    }

    private string BuildFileSummary(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return $"File: {filePath}\nSize: {info.Length} bytes\nModified: {info.LastWriteTime}";
        }
        catch (Exception ex)
        {
            return $"File: {filePath}\nError: {ex.Message}";
        }
    }

    private string BuildSymbolSummary(CodeSymbol sym)
    {
        var header = $"Name: {sym.Name}\nKind: {sym.Kind}\nFile: {sym.FilePath}\nRange: L{sym.StartCodeLoc.Line}-{sym.EndCodeLoc.Line}\n";
        var snippet = ReadSnippet(sym.FilePath, sym.StartCodeLoc.Line, sym.EndCodeLoc.Line, maxLines: 80);
        return header + "\n" + snippet;
    }

    private string ReadSnippet(string filePath, int startLine, int endLine, int maxLines = 120)
    {
        try
        {
            if (!File.Exists(filePath)) return "<file not found>";
            var lines = File.ReadAllLines(filePath);
            int s = Math.Max(1, startLine);
            int e = Math.Min(lines.Length, Math.Max(s, endLine));
            int count = Math.Min(maxLines, e - s + 1);
            var slice = lines.Skip(s - 1).Take(count).ToArray();
            // Prepend line numbers
            return string.Join('\n', slice.Select((l, i) => $"{s + i,4}: {l}"));
        }
        catch (Exception ex)
        {
            return $"<error reading snippet: {ex.Message}>";
        }
    }

    private void ApplyFilter(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            LoadSymbols(_codeMap);
            return;
        }

        // naive filter: include files/symbols whose name contains the text
        var filtered = CodeMap.Create();
        foreach (var sym in _codeMap)
        {
            if (sym.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(sym.FilePath).Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                filtered.AddSymbol(sym);
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
        if (_availableSchemes.Count == 0) return;
        _schemeIndex = (_schemeIndex + 1) % _availableSchemes.Count;
        ApplyScheme(_availableSchemes[_schemeIndex]);
        SetNeedsDraw();
    }

    private void ToggleColors()
    {
        Application.Force16Colors = !Application.Force16Colors;
        Application.Driver?.ClearContents();
        SetNeedsDraw();
    }

    private void SetupColorSchemes()
    {
        try
        {
            var dark = new Scheme(new Terminal.Gui.Drawing.Attribute(StandardColor.LightGray, StandardColor.Black));
            var names = SchemeManager.GetSchemeNames();
            if (!names.Contains("ThaumDark"))
            {
                SchemeManager.AddScheme("ThaumDark", dark);
            }

            _availableSchemes.Clear();
            _availableSchemes.Add("ThaumDark");
            var baseName = SchemeManager.SchemesToSchemeName(Schemes.Base);
            var topName  = SchemeManager.SchemesToSchemeName(Schemes.Toplevel);
            var dlgName  = SchemeManager.SchemesToSchemeName(Schemes.Dialog);
            if (baseName is { }) _availableSchemes.Add(baseName);
            if (topName  is { }) _availableSchemes.Add(topName);
            if (dlgName  is { }) _availableSchemes.Add(dlgName);

            ApplyScheme(_availableSchemes[0]);
        }
        catch
        {
            // ignore if configuration not initialized
        }
    }

    private void ApplyScheme(string schemeName)
    {
        try
        {
            var scheme = SchemeManager.GetScheme(schemeName);
            SetScheme(scheme);
            _tree?.SetScheme(scheme);
            _tabs?.SetScheme(scheme);
            _status?.SetScheme(scheme);
        }
        catch { }
    }

    private void EnqueueJob(CodeSymbol symbol, string promptName)
    {
        var job = new JobItem(symbol, promptName);
        _jobs.Add(job);
        RefreshJobsList();

        // Simulate async processing and completion
        Task.Run(async () =>
        {
            job.Status = JobStatus.Running;
            UpdateJob(job);
            await Task.Delay(1200);
            job.Status = JobStatus.Succeeded;
            UpdateJob(job);
        });
    }

    private void UpdateJob(JobItem job)
    {
        Application.Invoke(() => RefreshJobsList());
    }

    private void RefreshJobsList()
    {
        _jobsList.SetSource(new ObservableCollection<string>(_jobs.Select(j => j.ToString()).ToList()));
        _jobsList.SetNeedsDraw();
    }
}

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Canceled
}

public class JobItem
{
    public string     Id         { get; } = Guid.NewGuid().ToString("N")[..8];
    public CodeSymbol Symbol     { get; }
    public string     PromptName { get; }
    public JobStatus  Status     { get; set; } = JobStatus.Queued;

    public JobItem(CodeSymbol symbol, string promptName)
    {
        Symbol     = symbol;
        PromptName = promptName;
    }

    public override string ToString()
    {
        var name = Symbol.Name;
        return $"[{Status}] {Id} {name} :: {PromptName}";
    }
}
