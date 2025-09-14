using System.Text;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Ratatui;
using Ratatui.Layout;
using Thaum.Utils;

namespace Thaum.App.RatatuiTUI;

public class RatatuiApp
{
    private readonly Crawler _crawler;
    private readonly Compressor _compressor;
    private readonly ILogger _logger;

    private enum Panel { Files, Symbols }
    private enum Mode { Browser, Source, Summary, References, Info }

    private sealed class AppState
    {
        public Panel Focus = Panel.Files;
        public Mode Screen = Mode.Browser;

        public List<string> AllFiles = new();
        public List<string> VisibleFiles = new();
        public int FileSelected;
        public int FileOffset;
        public string FileFilter = string.Empty;

        public List<CodeSymbol> AllSymbols = new();
        public List<CodeSymbol> VisibleSymbols = new();
        public int SymSelected;
        public int SymOffset;
        public string SymFilter = string.Empty;

        public string? Summary;
        public bool IsLoading;

        // Operation screens state
        public List<string>? SourceLines;
        public int SourceSelected;
        public int SourceOffset;

        public List<(string File,int Line,string Name)>? Refs;
        public int RefsSelected;
        public int RefsOffset;
    }

    public RatatuiApp(Crawler crawler, Compressor compressor, ILogger logger)
    {
        _crawler = crawler;
        _compressor = compressor;
        _logger = logger;
    }

    // Global, semantically meaningful colors/styles
    private static class Theme
    {
        public static readonly Style Hint = new Style(dim: true);
        public static readonly Style FilePath = new Style(fg: Color.Cyan);
        public static readonly Style LineNumber = new Style(fg: Color.DarkGray);
        public static readonly Style Error = new Style(fg: Color.LightRed, bold: true);
        public static readonly Style Success = new Style(fg: Color.LightGreen, bold: true);
        public static readonly Style Info = new Style(fg: Color.LightBlue);
        public static readonly Style Title = new Style(bold: true);
    }

    public async Task RunAsync(CodeMap codeMap, string projectPath, string language)
    {
        var allSymbols = codeMap.ToList()
            .OrderBy(s => (s.FilePath, s.StartCodeLoc.Line))
            .ToList();
        if (allSymbols.Count == 0)
        {
            Console.WriteLine("No symbols to display");
            return;
        }

        using var term = new Terminal().Raw().AltScreen().ShowCursor(false);

        var app = new AppState
        {
            AllSymbols = allSymbols,
            AllFiles = allSymbols.Select(s => s.FilePath).Distinct().OrderBy(x => x).ToList(),
            Summary = null,
            IsLoading = false
        };
        app.VisibleFiles = app.AllFiles.ToList();
        var firstFile = app.VisibleFiles.FirstOrDefault();
        app.VisibleSymbols = firstFile == null ? new List<CodeSymbol>() : SymbolsForFile(app, firstFile).ToList();

        List? filesList = BuildFilesList(app, projectPath);
        List? symList = BuildSymbolsList(app);
        using var filesState = new ListState().Selected(app.FileSelected).Offset(app.FileOffset);
        using var symState = new ListState().Selected(app.SymSelected).Offset(app.SymOffset);

        bool redraw = true;
        var poll = TimeSpan.FromMilliseconds(75);

        try
        {
            while (true)
            {
                if (redraw)
                {
                    Draw(term, projectPath, filesList!, filesState, symList!, symState, app);
                    redraw = false;
                }

                if (!term.NextEvent(poll, out var ev))
                {
                    if (app.IsLoading) redraw = true;
                    continue;
                }

                if (ev.Kind == EventKind.Resize)
                {
                    redraw = true;
                    continue;
                }

                if (ev.Kind == EventKind.Key)
                {
                    switch (ev.Key.CodeEnum)
                    {
                        case KeyCode.Down:
                            if (app.Screen == Mode.Source && app.SourceLines is { Count: > 0 })
                            { app.SourceSelected = Math.Min(app.SourceSelected + 1, app.SourceLines.Count - 1); EnsureVisible(ref app.SourceOffset, app.SourceSelected); redraw = true; break; }
                            if (app.Screen == Mode.References && app.Refs is { Count: > 0 })
                            { app.RefsSelected = Math.Min(app.RefsSelected + 1, app.Refs.Count - 1); EnsureVisible(ref app.RefsOffset, app.RefsSelected); redraw = true; break; }
                            if (app.Focus == Panel.Files && app.VisibleFiles.Count > 0)
                            {
                                app.FileSelected = Math.Min(app.FileSelected + 1, app.VisibleFiles.Count - 1);
                                filesState.Selected(app.FileSelected);
                                EnsureVisible(ref app.FileOffset, app.FileSelected);
                                filesState.Offset(app.FileOffset);
                                redraw = true;
                            }
                            else if (app.Focus == Panel.Symbols && app.VisibleSymbols.Count > 0)
                            {
                                app.SymSelected = Math.Min(app.SymSelected + 1, app.VisibleSymbols.Count - 1);
                                symState.Selected(app.SymSelected);
                                EnsureVisible(ref app.SymOffset, app.SymSelected);
                                symState.Offset(app.SymOffset);
                                app.Summary = null;
                                redraw = true;
                            }
                            break;
                        case KeyCode.Up:
                            if (app.Screen == Mode.Source && app.SourceLines is { Count: > 0 })
                            { app.SourceSelected = Math.Max(app.SourceSelected - 1, 0); EnsureVisible(ref app.SourceOffset, app.SourceSelected); redraw = true; break; }
                            if (app.Screen == Mode.References && app.Refs is { Count: > 0 })
                            { app.RefsSelected = Math.Max(app.RefsSelected - 1, 0); EnsureVisible(ref app.RefsOffset, app.RefsSelected); redraw = true; break; }
                            if (app.Focus == Panel.Files && app.VisibleFiles.Count > 0)
                            {
                                app.FileSelected = Math.Max(app.FileSelected - 1, 0);
                                filesState.Selected(app.FileSelected);
                                EnsureVisible(ref app.FileOffset, app.FileSelected);
                                filesState.Offset(app.FileOffset);
                                redraw = true;
                            }
                            else if (app.Focus == Panel.Symbols && app.VisibleSymbols.Count > 0)
                            {
                                app.SymSelected = Math.Max(app.SymSelected - 1, 0);
                                symState.Selected(app.SymSelected);
                                EnsureVisible(ref app.SymOffset, app.SymSelected);
                                symState.Offset(app.SymOffset);
                                app.Summary = null;
                                redraw = true;
                            }
                            break;
                        case KeyCode.PageDown:
                            if (app.Screen == Mode.Source && app.SourceLines is { Count: > 0 })
                            { app.SourceSelected = Math.Min(app.SourceSelected + 10, app.SourceLines.Count - 1); EnsureVisible(ref app.SourceOffset, app.SourceSelected); redraw = true; break; }
                            if (app.Screen == Mode.References && app.Refs is { Count: > 0 })
                            { app.RefsSelected = Math.Min(app.RefsSelected + 10, app.Refs.Count - 1); EnsureVisible(ref app.RefsOffset, app.RefsSelected); redraw = true; break; }
                            if (app.Focus == Panel.Files && app.VisibleFiles.Count > 0)
                            {
                                app.FileSelected = Math.Min(app.FileSelected + 10, app.VisibleFiles.Count - 1);
                                filesState.Selected(app.FileSelected);
                                EnsureVisible(ref app.FileOffset, app.FileSelected);
                                filesState.Offset(app.FileOffset);
                                redraw = true;
                            }
                            else if (app.Focus == Panel.Symbols && app.VisibleSymbols.Count > 0)
                            {
                                app.SymSelected = Math.Min(app.SymSelected + 10, app.VisibleSymbols.Count - 1);
                                symState.Selected(app.SymSelected);
                                EnsureVisible(ref app.SymOffset, app.SymSelected);
                                symState.Offset(app.SymOffset);
                                app.Summary = null;
                                redraw = true;
                            }
                            break;
                        case KeyCode.PageUp:
                            if (app.Screen == Mode.Source && app.SourceLines is { Count: > 0 })
                            { app.SourceSelected = Math.Max(app.SourceSelected - 10, 0); EnsureVisible(ref app.SourceOffset, app.SourceSelected); redraw = true; break; }
                            if (app.Screen == Mode.References && app.Refs is { Count: > 0 })
                            { app.RefsSelected = Math.Max(app.RefsSelected - 10, 0); EnsureVisible(ref app.RefsOffset, app.RefsSelected); redraw = true; break; }
                            if (app.Focus == Panel.Files && app.VisibleFiles.Count > 0)
                            {
                                app.FileSelected = Math.Max(app.FileSelected - 10, 0);
                                filesState.Selected(app.FileSelected);
                                EnsureVisible(ref app.FileOffset, app.FileSelected);
                                filesState.Offset(app.FileOffset);
                                redraw = true;
                            }
                            else if (app.Focus == Panel.Symbols && app.VisibleSymbols.Count > 0)
                            {
                                app.SymSelected = Math.Max(app.SymSelected - 10, 0);
                                symState.Selected(app.SymSelected);
                                EnsureVisible(ref app.SymOffset, app.SymSelected);
                                symState.Offset(app.SymOffset);
                                app.Summary = null;
                                redraw = true;
                            }
                            break;
                        case KeyCode.Tab:
                            if (app.Screen != Mode.Browser) { app.Screen = Mode.Browser; redraw = true; break; }
                            app.Focus = app.Focus == Panel.Files ? Panel.Symbols : Panel.Files;
                            redraw = true;
                            break;
                        case KeyCode.Enter:
                        case KeyCode.Right:
                            if (app.Focus == Panel.Files && app.VisibleFiles.Count > 0)
                            {
                                var file = app.VisibleFiles[app.FileSelected];
                                app.VisibleSymbols = SymbolsForFile(app, file).ToList();
                                app.SymSelected = 0; app.SymOffset = 0; app.Summary = null;
                                symList?.Dispose(); symList = BuildSymbolsList(app);
                                symState.Selected(app.SymSelected).Offset(app.SymOffset);
                                redraw = true;
                            }
                            else if (app.Focus == Panel.Symbols && app.VisibleSymbols.Count > 0 && !app.IsLoading)
                            {
                                app.IsLoading = true; redraw = true;
                                _ = Task.Run(async () =>
                                {
                                    try { app.Summary = await LoadSymbolDetail(app.VisibleSymbols[app.SymSelected]); }
                                    finally { app.IsLoading = false; }
                                });
                            }
                            break;
                        case KeyCode.Char when ev.Key.Char == (uint)'o':
                            // Open in editor from context
                            if (app.Screen == Mode.Browser && app.VisibleSymbols.Count > 0)
                            {
                                var s = app.VisibleSymbols[app.SymSelected];
                                OpenInEditor(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line), out var msg, out var ok);
                            }
                            else if (app.Screen == Mode.Source && app.VisibleSymbols.Count > 0)
                            {
                                var s = app.VisibleSymbols[app.SymSelected];
                                OpenInEditor(projectPath, s.FilePath, Math.Max(1, app.SourceSelected + 1), out var msg, out var ok);
                            }
                            else if (app.Screen == Mode.References && app.Refs is { Count: > 0 })
                            {
                                var (f, ln, _) = app.Refs[Math.Min(app.RefsSelected, app.Refs.Count - 1)];
                                OpenInEditor(projectPath, f, Math.Max(1, ln), out var msg, out var ok);
                            }
                            redraw = true; break;
                        case KeyCode.Esc:
                            return;
                        case KeyCode.Char:
                            if (ev.Key.Char == (uint)'q') return;
                            if (ev.Key.Char == (uint)'1') { app.Screen = Mode.Browser; redraw = true; break; }
                            if (ev.Key.Char == (uint)'2') { await EnsureSource(app); app.Screen = Mode.Source; redraw = true; break; }
                            if (ev.Key.Char == (uint)'3') { if (!app.IsLoading && app.VisibleSymbols.Count > 0 && string.IsNullOrEmpty(app.Summary)) { app.IsLoading = true; _ = Task.Run(async () => { try { app.Summary = await LoadSymbolDetail(app.VisibleSymbols[app.SymSelected]); } finally { app.IsLoading = false; } }); } app.Screen = Mode.Summary; redraw = true; break; }
                            if (ev.Key.Char == (uint)'4') { await EnsureRefs(app); app.Screen = Mode.References; redraw = true; break; }
                            if (ev.Key.Char == (uint)'5') { app.Screen = Mode.Info; redraw = true; break; }
                            if (ev.Key.Char == (uint)'/')
                            {
                                if (app.Screen != Mode.Browser) { app.Screen = Mode.Browser; redraw = true; break; }
                                if (app.Focus == Panel.Files)
                                { app.FileFilter = string.Empty; ApplyFileFilter(app); filesList?.Dispose(); filesList = BuildFilesList(app, projectPath); filesState.Selected(app.FileSelected).Offset(app.FileOffset); }
                                else
                                { app.SymFilter = string.Empty; ApplySymbolFilter(app); symList?.Dispose(); symList = BuildSymbolsList(app); symState.Selected(app.SymSelected).Offset(app.SymOffset); }
                                redraw = true; break;
                            }
                            char ch = (char)ev.Key.Char;
                            if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/')
                            {
                                if (app.Screen != Mode.Browser) { /* ignore typing in op screens */ }
                                else if (app.Focus == Panel.Files)
                                { app.FileFilter += ch; ApplyFileFilter(app); filesList?.Dispose(); filesList = BuildFilesList(app, projectPath); filesState.Selected(app.FileSelected).Offset(app.FileOffset); }
                                else
                                { app.SymFilter += ch; ApplySymbolFilter(app); symList?.Dispose(); symList = BuildSymbolsList(app); symState.Selected(app.SymSelected).Offset(app.SymOffset); }
                                redraw = true;
                            }
                            break;
                        case KeyCode.Backspace:
                            if (app.Screen != Mode.Browser) { /* ignore */ }
                            else if (app.Focus == Panel.Files && app.FileFilter.Length > 0)
                            { app.FileFilter = app.FileFilter[..^1]; ApplyFileFilter(app); filesList?.Dispose(); filesList = BuildFilesList(app, projectPath); filesState.Selected(app.FileSelected).Offset(app.FileOffset); redraw = true; }
                            else if (app.Focus == Panel.Symbols && app.SymFilter.Length > 0)
                            { app.SymFilter = app.SymFilter[..^1]; ApplySymbolFilter(app); symList?.Dispose(); symList = BuildSymbolsList(app); symState.Selected(app.SymSelected).Offset(app.SymOffset); redraw = true; }
                            break;
                    }
                }
            }
        }
        finally
        {
            filesList?.Dispose();
            symList?.Dispose();
        }
    }

    private async Task<string> LoadSymbolDetail(CodeSymbol symbol)
    {
        try
        {
            string? source = await _crawler.GetCode(symbol);
            if (string.IsNullOrEmpty(source)) return "No source available for symbol.";
            var ctx = new OptimizationContext(Level: symbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
                AvailableKeys: [],
                PromptName: GLB.GetDefaultPrompt(symbol));
            string summary = await _compressor.OptimizeSymbolAsync(symbol, ctx, source);
            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error summarizing symbol {Name}", symbol.Name);
            return $"Error: {ex.Message}";
        }
    }

    private static void Draw(Terminal term, string projectPath, List filesList, ListState filesState, List symList, ListState symState, AppState app)
    {
        var (w, h) = term.Size();
        var area = Rect.FromSize(Math.Max(1, w), Math.Max(1, h));

        var rows = Layout.SplitHorizontal(area, stackalloc Constraint[]
        {
            Constraint.Length(3),
            Constraint.Percentage(100),
            Constraint.Length(1)
        }, gap: 0, margin: 0);

        var focusText = app.Focus == Panel.Files ? "Files" : "Symbols";
        var modeText = app.Screen switch { Mode.Browser => "Browser", Mode.Source => "Source", Mode.Summary => "Summary", Mode.References => "References", Mode.Info => "Info", _ => "" };
        using var header = new Paragraph($"Thaum — {Path.GetFileName(projectPath)}  Mode: {modeText}  Focus: {focusText}")
            .AppendLine("1 Browser 2 Source 3 Summary 4 Refs 5 Info    Tab switch    / filter    o open  q quit", Theme.Hint);
        term.Draw(header, rows[0]);

        if (app.Screen == Mode.Browser)
        {
            var cols = Layout.SplitVertical(rows[1], stackalloc Constraint[] { Constraint.Percentage(30), Constraint.Percentage(30), Constraint.Percentage(40) }, gap: 1, margin: 0);
            using (var title = new Paragraph(app.FileFilter.Length == 0 ? "(type to filter)" : $"/{app.FileFilter}").Title("Files", border: true))
            { term.Draw(title, new Rect(cols[0].X, cols[0].Y, cols[0].Width, 2)); }
            term.Draw(filesList, new Rect(cols[0].X, cols[0].Y + 2, cols[0].Width, cols[0].Height - 2), filesState);

            using (var title = new Paragraph(app.SymFilter.Length == 0 ? "(type to filter)" : $"/{app.SymFilter}").Title("Symbols", border: true))
            { term.Draw(title, new Rect(cols[1].X, cols[1].Y, cols[1].Width, 2)); }
            term.Draw(symList, new Rect(cols[1].X, cols[1].Y + 2, cols[1].Width, cols[1].Height - 2), symState);

            var hasSym = app.VisibleSymbols.Count > 0;
            using var meta = new Paragraph("").Title("Details", border: true)
                .AppendLine(!hasSym ? "" : $"Name: {app.VisibleSymbols[app.SymSelected].Name}")
                .AppendLine(!hasSym ? "" : $"Kind: {app.VisibleSymbols[app.SymSelected].Kind}")
                .AppendLine(!hasSym ? "" : $"File: {app.VisibleSymbols[app.SymSelected].FilePath}")
                .AppendLine(!hasSym ? "" : $"Span: L{app.VisibleSymbols[app.SymSelected].StartCodeLoc.Line}:C{app.VisibleSymbols[app.SymSelected].StartCodeLoc.Character}");

            var right = cols[2];
            var metaHeight = Math.Max(6, right.Height / 3);
            term.Draw(meta, new Rect(right.X, right.Y, right.Width, metaHeight));
            var body = app.IsLoading ? $"Summarizing… {Spinner()}" : (app.Summary ?? "");
            using var detail = new Paragraph(body).Title("Summary", border: true);
            term.Draw(detail, new Rect(right.X, right.Y + metaHeight + 1, right.Width, Math.Max(1, right.Height - metaHeight - 1)));
        }
        else if (app.Screen == Mode.Source)
        {
            using var title = new Paragraph("Source").Title("Source", border: true);
            term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
            var lines = app.SourceLines ?? new List<string>();
            using var list = new List();
            int start = Math.Max(0, app.SourceOffset);
            int end = Math.Min(lines.Count, start + Math.Max(1, rows[1].Height - 3));
            for (int i = start; i < end; i++)
            {
                var num = (i + 1).ToString().PadLeft(5) + "  ";
                var ln = Encoding.UTF8.GetBytes(num).AsMemory();
                var code = Encoding.UTF8.GetBytes(lines[i]).AsMemory();
                ReadOnlyMemory<Ratatui.Batching.SpanRun> runs = new[]
                {
                    new Ratatui.Batching.SpanRun(ln, Theme.LineNumber),
                    new Ratatui.Batching.SpanRun(code, default)
                };
                list.AppendItem(runs.Span);
            }
            term.Draw(list, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
        }
        else if (app.Screen == Mode.Summary)
        {
            using var title = new Paragraph("Summary").Title("Summary", border: true);
            term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
            var body = app.IsLoading ? $"Summarizing… {Spinner()}" : (app.Summary ?? "No summary yet. Press 3 to (re)generate.");
            using var para = new Paragraph(body);
            term.Draw(para, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
        }
        else if (app.Screen == Mode.References)
        {
            using var title = new Paragraph("References").Title("References", border: true);
            term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
            using var list = new List();
            var refs = app.Refs ?? new List<(string,int,string)>();
            int start = Math.Max(0, app.RefsOffset);
            int end = Math.Min(refs.Count, start + Math.Max(1, rows[1].Height - 2));
            for (int i = start; i < end; i++)
            {
                var (f, ln, nm) = refs[i];
                var lhs = Encoding.UTF8.GetBytes($"{Path.GetFileName(f)}:{ln}  ").AsMemory();
                var rhs = Encoding.UTF8.GetBytes(nm).AsMemory();
                ReadOnlyMemory<Ratatui.Batching.SpanRun> runs = new[]
                {
                    new Ratatui.Batching.SpanRun(lhs, Theme.FilePath),
                    new Ratatui.Batching.SpanRun(rhs, default)
                };
                list.AppendItem(runs.Span);
            }
            term.Draw(list, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
        }
        else if (app.Screen == Mode.Info)
        {
            using var title = new Paragraph("Info").Title("Info", border: true);
            term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
            if (app.VisibleSymbols.Count > 0)
            {
                var s = app.VisibleSymbols[app.SymSelected];
                using var para = new Paragraph("")
                    .AppendLine($"Name: {s.Name}")
                    .AppendLine($"Kind: {s.Kind}")
                    .AppendLine($"File: {s.FilePath}")
                    .AppendLine($"Start: L{s.StartCodeLoc.Line}:C{s.StartCodeLoc.Character}")
                    .AppendLine($"End:   L{s.EndCodeLoc.Line}:C{s.EndCodeLoc.Character}")
                    .AppendLine($"Children: {s.Children?.Count ?? 0}")
                    .AppendLine($"Deps: {s.Dependencies?.Count ?? 0}")
                    .AppendLine($"Last: {(s.LastModified?.ToString("u") ?? "n/a")}");
                term.Draw(para, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
            }
        }

        using var footer = new Paragraph(app.Screen switch
        {
            Mode.Browser => "↑/↓ move  Tab switch  / filter  Enter open/summarize  2 Source 3 Summary 4 Refs 5 Info",
            Mode.Source => "↑/↓ scroll  1 Browser 3 Summary 4 Refs 5 Info",
            Mode.Summary => (app.IsLoading ? "Summarizing…" : "") + " 1 Browser 2 Source 4 Refs 5 Info",
            Mode.References => "↑/↓ scroll  1 Browser 2 Source 3 Summary 5 Info",
            Mode.Info => "1 Browser 2 Source 3 Summary 4 Refs",
            _ => ""
        }).AppendSpan("   o open   ", Theme.Hint)
          .AppendSpan("Ratatui.cs", Theme.Hint);
        term.Draw(footer, rows[2]);
    }

    private static IEnumerable<CodeSymbol> SymbolsForFile(AppState app, string? file)
        => string.IsNullOrEmpty(file) ? app.AllSymbols : app.AllSymbols.Where(s => s.FilePath == file);

    private static string FileLine(string projectPath, string path)
    {
        try { return Path.GetRelativePath(projectPath, path); }
        catch { return Path.GetFileName(path); }
    }

    private static List BuildFilesList(AppState app, string projectPath)
    {
        var list = new List().Title("Files").HighlightSymbol("→ ").HighlightStyle(new Style(bold: true));
        foreach (var f in app.VisibleFiles) list.AppendItem(FileLine(projectPath, f));
        return list;
    }

    private static List BuildSymbolsList(AppState app)
    {
        var list = new List().Title("Symbols").HighlightSymbol("→ ").HighlightStyle(new Style(bold: true));
        foreach (var s in app.VisibleSymbols) list.AppendItem(SymbolLine(s), StyleForKind(s.Kind));
        return list;
    }

    private static void ApplyFileFilter(AppState app)
    {
        if (string.IsNullOrWhiteSpace(app.FileFilter)) app.VisibleFiles = app.AllFiles.ToList();
        else { var f = app.FileFilter.ToLowerInvariant(); app.VisibleFiles = app.AllFiles.Where(p => p.ToLowerInvariant().Contains(f)).ToList(); }
        app.FileSelected = 0; app.FileOffset = 0; app.Summary = null;
        var file = app.VisibleFiles.FirstOrDefault();
        app.VisibleSymbols = file == null ? new List<CodeSymbol>() : SymbolsForFile(app, file).ToList();
        app.SymSelected = 0; app.SymOffset = 0;
    }

    private static void ApplySymbolFilter(AppState app)
    {
        string? file = app.VisibleFiles.Count == 0 ? null : app.VisibleFiles[Math.Min(app.FileSelected, app.VisibleFiles.Count - 1)];
        var baseSet = SymbolsForFile(app, file);
        if (string.IsNullOrWhiteSpace(app.SymFilter)) app.VisibleSymbols = baseSet.ToList();
        else { var f = app.SymFilter.ToLowerInvariant(); app.VisibleSymbols = baseSet.Where(s => s.Name.ToLowerInvariant().Contains(f)).ToList(); }
        app.SymSelected = 0; app.SymOffset = 0; app.Summary = null;
    }

    private static string SymbolLine(CodeSymbol s)
    {
        var name = s.Name.Replace('\n', ' ');
        return $"{Icon(s)} {name}";
    }

    private static string Icon(CodeSymbol s) => s.Kind switch
    {
        SymbolKind.Class => "[C]",
        SymbolKind.Method => "[M]",
        SymbolKind.Function => "[F]",
        SymbolKind.Interface => "[I]",
        SymbolKind.Enum => "[E]",
        _ => "[·]"
    };

    private static Style StyleForKind(SymbolKind k) => k switch
    {
        SymbolKind.Class => new Style(fg: Color.LightYellow),
        SymbolKind.Method => new Style(fg: Color.LightGreen),
        SymbolKind.Function => new Style(fg: Color.LightGreen),
        SymbolKind.Interface => new Style(fg: Color.LightBlue),
        SymbolKind.Enum => new Style(fg: Color.Magenta),
        SymbolKind.Property => new Style(fg: Color.White),
        SymbolKind.Field => new Style(fg: Color.White),
        SymbolKind.Variable => new Style(fg: Color.White),
        _ => new Style(fg: Color.Gray)
    };

    private async Task EnsureSource(AppState app)
    {
        if (app.VisibleSymbols.Count == 0) { app.SourceLines = new List<string>(); return; }
        if (app.SourceLines != null && app.SourceLines.Count > 0) return;
        var s = app.VisibleSymbols[app.SymSelected];
        string? src = await _crawler.GetCode(s);
        app.SourceLines = (src ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
        app.SourceSelected = 0; app.SourceOffset = 0;
    }

    private async Task EnsureRefs(AppState app)
    {
        if (app.VisibleSymbols.Count == 0) { app.Refs = new List<(string,int,string)>(); return; }
        if (app.Refs != null && app.Refs.Count > 0) return;
        var s = app.VisibleSymbols[app.SymSelected];
        var refs = await _crawler.GetReferencesFor(s.Name, s.StartCodeLoc);
        app.Refs = refs.Select(r => (r.FilePath, r.StartCodeLoc.Line, r.Name)).ToList();
        app.RefsSelected = 0; app.RefsOffset = 0;
    }

    private static void EnsureVisible(ref int offset, int selected)
    {
        if (selected < offset) offset = selected;
        int max = offset + 20;
        if (selected >= max) offset = Math.Max(0, selected - 19);
    }

    private static string Truncate(string s, int width)
    {
        if (width <= 1) return s;
        return s.Length <= width ? s : s[..Math.Max(0, width - 1)] + "…";
    }

    private static List<string> Wrap(string s, int width)
    {
        if (width < 5) return new List<string> { Truncate(s, width) };
        var words = s.Replace("\r", string.Empty).Split('\n');
        var lines = new List<string>();
        foreach (var para in words)
        {
            var parts = para.Split(' ');
            var line = new StringBuilder();
            foreach (var p in parts)
            {
                if (line.Length + p.Length + 1 > width)
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(p);
            }
            if (line.Length > 0) lines.Add(line.ToString());
            lines.Add(string.Empty);
        }
        return lines;
    }

    private static string Spinner()
    {
        int t = (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 120) % 4);
        return "-\\|/"[t].ToString();
    }

    private static void OpenInEditor(string projectPath, string filePath, int line, out string message, out bool success)
    {
        success = false;
        message = string.Empty;
        try
        {
            string full = Path.IsPathRooted(filePath) ? filePath : Path.Combine(projectPath, filePath);
            string? cmd = Environment.GetEnvironmentVariable("THAUM_EDITOR") ?? Environment.GetEnvironmentVariable("EDITOR");
            string args;
            if (string.IsNullOrWhiteSpace(cmd))
            {
                // Try known editors
                if (File.Exists("/usr/bin/code") || File.Exists("/usr/local/bin/code"))
                { cmd = "code"; args = $"-g \"{full}\":{line}"; }
                else if (File.Exists("/usr/bin/nvim") || File.Exists("/usr/local/bin/nvim"))
                { cmd = "nvim"; args = $"+{line} \"{full}\""; }
                else if (File.Exists("/usr/bin/vim") || File.Exists("/usr/local/bin/vim"))
                { cmd = "vim"; args = $"+{line} \"{full}\""; }
                else
                { message = $"Open: {full}:{line}"; return; }
            }
            else
            {
                // Heuristic per editor
                if (cmd.Contains("code")) args = $"-g \"{full}\":{line}";
                else if (cmd.Contains("nvim") || cmd.Contains("vim")) args = $"+{line} \"{full}\"";
                else args = $"\"{full}\""; // generic
            }

            var psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
            {
                UseShellExecute = false,
                RedirectStandardError = false,
                RedirectStandardOutput = false,
            };
            System.Diagnostics.Process.Start(psi);
            success = true;
            message = $"Opened {Path.GetFileName(full)}:{line}";
        }
        catch (Exception ex)
        {
            message = $"Open failed: {ex.Message}";
        }
    }
}
