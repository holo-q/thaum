using Ratatui;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

internal sealed class TuiController
{
    private readonly Func<CodeSymbol, Task<string>> _summarize;
    private readonly Func<Task> _ensureSource;
    private readonly Func<Task> _ensureRefs;
    private readonly IEditorOpener _opener;
    private readonly string _projectPath;

    public TuiController(string projectPath, IEditorOpener opener,
        Func<CodeSymbol, Task<string>> summarize, Func<Task> ensureSource, Func<Task> ensureRefs)
    {
        _projectPath = projectPath; _opener = opener; _summarize = summarize; _ensureSource = ensureSource; _ensureRefs = ensureRefs;
    }

    public bool HandleKey(Event ev, RatatuiApp.AppState app)
    {
        switch (ev.Key.CodeEnum)
        {
            case KeyCode.Down:
                if (app is { screen: RatatuiApp.Mode.Source, sourceLines.Count: > 0 }) { app.sourceSelected = Math.Min(app.sourceSelected + 1, app.sourceLines!.Count - 1); RatatuiApp.EnsureVisible(ref app.sourceOffset, app.sourceSelected); return true; }
                if (app is { screen: RatatuiApp.Mode.References, refs.Count: > 0 }) { app.refsSelected = Math.Min(app.refsSelected + 1, app.refs!.Count - 1); RatatuiApp.EnsureVisible(ref app.refsOffset, app.refsSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Files, visibleFiles.Count: > 0 }) { app.fileSelected = Math.Min(app.fileSelected + 1, app.visibleFiles.Count - 1); RatatuiApp.EnsureVisible(ref app.fileOffset, app.fileSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Symbols, visibleSymbols.Count: > 0 }) { app.symSelected = Math.Min(app.symSelected + 1, app.visibleSymbols.Count - 1); RatatuiApp.EnsureVisible(ref app.symOffset, app.symSelected); app.summary = null; return true; }
                break;
            case KeyCode.Up:
                if (app is { screen: RatatuiApp.Mode.Source, sourceLines.Count: > 0 }) { app.sourceSelected = Math.Max(app.sourceSelected - 1, 0); RatatuiApp.EnsureVisible(ref app.sourceOffset, app.sourceSelected); return true; }
                if (app is { screen: RatatuiApp.Mode.References, refs.Count: > 0 }) { app.refsSelected = Math.Max(app.refsSelected - 1, 0); RatatuiApp.EnsureVisible(ref app.refsOffset, app.refsSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Files, visibleFiles.Count: > 0 }) { app.fileSelected = Math.Max(app.fileSelected - 1, 0); RatatuiApp.EnsureVisible(ref app.fileOffset, app.fileSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Symbols, visibleSymbols.Count: > 0 }) { app.symSelected = Math.Max(app.symSelected - 1, 0); RatatuiApp.EnsureVisible(ref app.symOffset, app.symSelected); app.summary = null; return true; }
                break;
            case KeyCode.PageDown:
                if (app is { screen: RatatuiApp.Mode.Source, sourceLines.Count: > 0 }) { app.sourceSelected = Math.Min(app.sourceSelected + 10, app.sourceLines!.Count - 1); RatatuiApp.EnsureVisible(ref app.sourceOffset, app.sourceSelected); return true; }
                if (app is { screen: RatatuiApp.Mode.References, refs.Count: > 0 }) { app.refsSelected = Math.Min(app.refsSelected + 10, app.refs!.Count - 1); RatatuiApp.EnsureVisible(ref app.refsOffset, app.refsSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Files, visibleFiles.Count: > 0 }) { app.fileSelected = Math.Min(app.fileSelected + 10, app.visibleFiles.Count - 1); RatatuiApp.EnsureVisible(ref app.fileOffset, app.fileSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Symbols, visibleSymbols.Count: > 0 }) { app.symSelected = Math.Min(app.symSelected + 10, app.visibleSymbols.Count - 1); RatatuiApp.EnsureVisible(ref app.symOffset, app.symSelected); app.summary = null; return true; }
                break;
            case KeyCode.PageUp:
                if (app is { screen: RatatuiApp.Mode.Source, sourceLines.Count: > 0 }) { app.sourceSelected = Math.Max(app.sourceSelected - 10, 0); RatatuiApp.EnsureVisible(ref app.sourceOffset, app.sourceSelected); return true; }
                if (app is { screen: RatatuiApp.Mode.References, refs.Count: > 0 }) { app.refsSelected = Math.Max(app.refsSelected - 10, 0); RatatuiApp.EnsureVisible(ref app.refsOffset, app.refsSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Files, visibleFiles.Count: > 0 }) { app.fileSelected = Math.Max(app.fileSelected - 10, 0); RatatuiApp.EnsureVisible(ref app.fileOffset, app.fileSelected); return true; }
                if (app is { focus: RatatuiApp.Panel.Symbols, visibleSymbols.Count: > 0 }) { app.symSelected = Math.Max(app.symSelected - 10, 0); RatatuiApp.EnsureVisible(ref app.symOffset, app.symSelected); app.summary = null; return true; }
                break;
            case KeyCode.Tab:
                if (app.screen != RatatuiApp.Mode.Browser) { app.screen = RatatuiApp.Mode.Browser; return true; }
                app.focus = app.focus == RatatuiApp.Panel.Files ? RatatuiApp.Panel.Symbols : RatatuiApp.Panel.Files; return true;
            case KeyCode.Enter:
            case KeyCode.Right:
                if (app is { focus: RatatuiApp.Panel.Files, visibleFiles.Count: > 0 }) { string file = app.visibleFiles[app.fileSelected]; app.visibleSymbols = app.allSymbols.Where(s => s.FilePath == file).ToList(); app.symSelected = 0; app.symOffset = 0; app.summary = null; return true; }
                if (app is { focus: RatatuiApp.Panel.Symbols, visibleSymbols.Count: > 0, isLoading: false }) { app.isLoading = true; _ = Task.Run(async () => { try { app.summary = await _summarize(app.visibleSymbols[app.symSelected]); } finally { app.isLoading = false; } }); return true; }
                break;
            case KeyCode.Char when ev.Key.Char == (uint)'o':
                if (app is { screen: RatatuiApp.Mode.Browser, visibleSymbols.Count: > 0 }) { var s = app.visibleSymbols[app.symSelected]; _opener.Open(_projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line)); return true; }
                if (app is { screen: RatatuiApp.Mode.Source, visibleSymbols.Count: > 0 }) { var s = app.visibleSymbols[app.symSelected]; _opener.Open(_projectPath, s.FilePath, Math.Max(1, app.sourceSelected + 1)); return true; }
                if (app is { screen: RatatuiApp.Mode.References, refs.Count: > 0 }) { var (f, ln, _) = app.refs![Math.Min(app.refsSelected, app.refs.Count - 1)]; _opener.Open(_projectPath, f, Math.Max(1, ln)); return true; }
                break;
            case KeyCode.Esc: Environment.Exit(0); break;
            case KeyCode.Char:
                if (ev.Key.Char == (uint)'q') Environment.Exit(0);
                if (ev.Key.Char == (uint)'1') { app.screen = RatatuiApp.Mode.Browser; return true; }
                if (ev.Key.Char == (uint)'2') { _ = _ensureSource(); app.screen = RatatuiApp.Mode.Source; return true; }
                if (ev.Key.Char == (uint)'3') { app.screen = RatatuiApp.Mode.Summary; if (!app.isLoading && app.visibleSymbols.Count > 0 && string.IsNullOrEmpty(app.summary)) { app.isLoading = true; _ = Task.Run(async () => { try { app.summary = await _summarize(app.visibleSymbols[app.symSelected]); } finally { app.isLoading = false; } }); } return true; }
                if (ev.Key.Char == (uint)'4') { _ = _ensureRefs(); app.screen = RatatuiApp.Mode.References; return true; }
                if (ev.Key.Char == (uint)'5') { app.screen = RatatuiApp.Mode.Info; return true; }
                if (ev.Key.Char == (uint)'/') {
                    if (app.screen != RatatuiApp.Mode.Browser) { app.screen = RatatuiApp.Mode.Browser; return true; }
                    if (app.focus == RatatuiApp.Panel.Files) { app.fileFilter = string.Empty; ApplyFileFilter(app); }
                    else { app.symFilter = string.Empty; ApplySymbolFilter(app); }
                    return true;
                }
                char ch = (char)ev.Key.Char;
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/') {
                    if (app.screen != RatatuiApp.Mode.Browser) return false;
                    if (app.focus == RatatuiApp.Panel.Files) { app.fileFilter += ch; ApplyFileFilter(app); }
                    else { app.symFilter += ch; ApplySymbolFilter(app); }
                    return true;
                }
                break;
            case KeyCode.Backspace:
                if (app.screen != RatatuiApp.Mode.Browser) return false;
                if (app is { focus: RatatuiApp.Panel.Files, fileFilter.Length: > 0 }) { app.fileFilter = app.fileFilter[..^1]; ApplyFileFilter(app); return true; }
                if (app is { focus: RatatuiApp.Panel.Symbols, symFilter.Length: > 0 }) { app.symFilter = app.symFilter[..^1]; ApplySymbolFilter(app); return true; }
                break;
        }
        return false;
    }

    private static void ApplyFileFilter(RatatuiApp.AppState app)
    {
        app.visibleFiles = string.IsNullOrWhiteSpace(app.fileFilter)
            ? app.allFiles.ToList()
            : app.allFiles.Where(p => p.ToLowerInvariant().Contains(app.fileFilter.ToLowerInvariant())).ToList();
        app.fileSelected = 0; app.fileOffset = 0; app.summary = null;
        var file = app.visibleFiles.FirstOrDefault();
        app.visibleSymbols = file == null ? new List<CodeSymbol>() : app.allSymbols.Where(s => s.FilePath == file).ToList();
        app.symSelected = 0; app.symOffset = 0;
    }

    private static void ApplySymbolFilter(RatatuiApp.AppState app)
    {
        string? file = app.visibleFiles.Count == 0 ? null : app.visibleFiles[Math.Min(app.fileSelected, app.visibleFiles.Count - 1)];
        var baseSet = string.IsNullOrEmpty(file) ? app.allSymbols : app.allSymbols.Where(s => s.FilePath == file);
        app.visibleSymbols = string.IsNullOrWhiteSpace(app.symFilter)
            ? baseSet.ToList()
            : baseSet.Where(s => s.Name.ToLowerInvariant().Contains(app.symFilter.ToLowerInvariant())).ToList();
        app.symSelected = 0; app.symOffset = 0; app.summary = null;
    }
}

