using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Ratatui;
using Ratatui.Layout;
using Thaum.Utils;

namespace Thaum.App.RatatuiTUI;

public class RatatuiApp {
	private readonly Crawler    _crawler;
	private readonly Compressor _compressor;
	private readonly ILogger    _logger;

    public enum Panel { Files, Symbols }

    public enum Mode { Browser, Source, Summary, References, Info }

		public sealed class AppState {
		public Panel focus  = Panel.Files;
		public Mode  screen = Mode.Browser;

		public List<string> allFiles     = [];
		public List<string> visibleFiles = [];
		public int          fileSelected;
		public int          fileOffset;
		public string       fileFilter = string.Empty;

		public List<CodeSymbol> allSymbols     = [];
		public List<CodeSymbol> visibleSymbols = [];
		public int              symSelected;
		public int              symOffset;
		public string           symFilter = string.Empty;

		public string? summary;
		public bool    isLoading;

		// Operation screens state
		public List<string>? sourceLines;
		public int           sourceSelected;
		public int           sourceOffset;

		public List<(string File, int Line, string Name)>? refs;
		public int                                         refsSelected;
		public int                                         refsOffset;
	}

	public RatatuiApp(Crawler crawler, Compressor compressor, ILogger logger) {
		_crawler    = crawler;
		_compressor = compressor;
		_logger     = logger;
	}

		// Theme lives in TuiTheme now

	public async Task RunAsync(CodeMap codeMap, string projectPath, string language) {
		List<CodeSymbol> allSymbols = codeMap.ToList()
			.OrderBy(s => (s.FilePath, s.StartCodeLoc.Line))
			.ToList();

		if (allSymbols.Count == 0) {
			Console.WriteLine("No symbols to display");
			return;
		}

        using Terminal term = new Terminal().Raw().AltScreen().ShowCursor(false);
        var opener = new DefaultEditorOpener();

		AppState app = new AppState {
			allSymbols = allSymbols,
			allFiles   = allSymbols.Select(s => s.FilePath).Distinct().OrderBy(x => x).ToList(),
			summary    = null,
			isLoading  = false
		};

		app.visibleFiles = app.allFiles.ToList();
		string? firstFile = app.visibleFiles.FirstOrDefault();
		app.visibleSymbols = firstFile == null ? [] : SymbolsForFile(app, firstFile).ToList();

        // Browser lists are now built inside BrowserScreen; no local ListState needed.

		bool     redraw = true;
		TimeSpan poll   = TimeSpan.FromMilliseconds(75);

		try {
			while (true) {
				if (redraw) {
					Draw(term, projectPath, app);
					redraw = false;
				}

				if (!term.NextEvent(poll, out Event ev)) {
					if (app.isLoading) redraw = true;
					continue;
				}

				switch (ev.Kind) {
					case EventKind.Resize:
						redraw = true;
						continue;
					case EventKind.Key:
						switch (ev.Key.CodeEnum) {
							case KeyCode.Down:
								if (app is { screen: Mode.Source, sourceLines.Count: > 0 }) {
									app.sourceSelected = Math.Min(app.sourceSelected + 1, app.sourceLines.Count - 1);
									EnsureVisible(ref app.sourceOffset, app.sourceSelected);
									redraw = true;
									break;
								}
								if (app is { screen: Mode.References, refs.Count: > 0 }) {
									app.refsSelected = Math.Min(app.refsSelected + 1, app.refs.Count - 1);
									EnsureVisible(ref app.refsOffset, app.refsSelected);
									redraw = true;
									break;
								}
								switch (app) {
									case { focus: Panel.Files, visibleFiles.Count: > 0 }:
                                app.fileSelected = Math.Min(app.fileSelected + 1, app.visibleFiles.Count - 1);
                                EnsureVisible(ref app.fileOffset, app.fileSelected);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
                                app.symSelected = Math.Min(app.symSelected + 1, app.visibleSymbols.Count - 1);
                                EnsureVisible(ref app.symOffset, app.symSelected);
										app.summary = null;
										redraw      = true;
										break;
								}
								break;
							case KeyCode.Up:
								if (app is { screen: Mode.Source, sourceLines.Count: > 0 }) {
									app.sourceSelected = Math.Max(app.sourceSelected - 1, 0);
									EnsureVisible(ref app.sourceOffset, app.sourceSelected);
									redraw = true;
									break;
								}
								if (app is { screen: Mode.References, refs.Count: > 0 }) {
									app.refsSelected = Math.Max(app.refsSelected - 1, 0);
									EnsureVisible(ref app.refsOffset, app.refsSelected);
									redraw = true;
									break;
								}
								switch (app) {
									case { focus: Panel.Files, visibleFiles.Count: > 0 }:
                                app.fileSelected = Math.Max(app.fileSelected - 1, 0);
                                EnsureVisible(ref app.fileOffset, app.fileSelected);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
                                app.symSelected = Math.Max(app.symSelected - 1, 0);
                                EnsureVisible(ref app.symOffset, app.symSelected);
										app.summary = null;
										redraw      = true;
										break;
								}
								break;
							case KeyCode.PageDown:
								if (app is { screen: Mode.Source, sourceLines.Count: > 0 }) {
									app.sourceSelected = Math.Min(app.sourceSelected + 10, app.sourceLines.Count - 1);
									EnsureVisible(ref app.sourceOffset, app.sourceSelected);
									redraw = true;
									break;
								}
								if (app is { screen: Mode.References, refs.Count: > 0 }) {
									app.refsSelected = Math.Min(app.refsSelected + 10, app.refs.Count - 1);
									EnsureVisible(ref app.refsOffset, app.refsSelected);
									redraw = true;
									break;
								}
								switch (app) {
									case { focus: Panel.Files, visibleFiles.Count: > 0 }:
                                app.fileSelected = Math.Min(app.fileSelected + 10, app.visibleFiles.Count - 1);
                                EnsureVisible(ref app.fileOffset, app.fileSelected);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
                                app.symSelected = Math.Min(app.symSelected + 10, app.visibleSymbols.Count - 1);
                                EnsureVisible(ref app.symOffset, app.symSelected);
										app.summary = null;
										redraw      = true;
										break;
								}
								break;
							case KeyCode.PageUp:
								if (app is { screen: Mode.Source, sourceLines.Count: > 0 }) {
									app.sourceSelected = Math.Max(app.sourceSelected - 10, 0);
									EnsureVisible(ref app.sourceOffset, app.sourceSelected);
									redraw = true;
									break;
								}
								if (app is { screen: Mode.References, refs.Count: > 0 }) {
									app.refsSelected = Math.Max(app.refsSelected - 10, 0);
									EnsureVisible(ref app.refsOffset, app.refsSelected);
									redraw = true;
									break;
								}
								switch (app) {
									case { focus: Panel.Files, visibleFiles.Count: > 0 }:
                                app.fileSelected = Math.Max(app.fileSelected - 10, 0);
                                EnsureVisible(ref app.fileOffset, app.fileSelected);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
                                app.symSelected = Math.Max(app.symSelected - 10, 0);
                                EnsureVisible(ref app.symOffset, app.symSelected);
										app.summary = null;
										redraw      = true;
										break;
								}
								break;
							case KeyCode.Tab:
								if (app.screen != Mode.Browser) {
									app.screen = Mode.Browser;
									redraw     = true;
									break;
								}
								app.focus = app.focus == Panel.Files ? Panel.Symbols : Panel.Files;
								redraw    = true;
								break;
							case KeyCode.Enter:
							case KeyCode.Right:
								switch (app) {
									case { focus: Panel.Files, visibleFiles.Count: > 0 }: {
										string file = app.visibleFiles[app.fileSelected];
										app.visibleSymbols = SymbolsForFile(app, file).ToList();
										app.symSelected    = 0;
										app.symOffset      = 0;
                                app.summary        = null;
										redraw = true;
										break;
									}
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0, isLoading: false }:
										app.isLoading = true;
										redraw        = true;
										_ = Task.Run(async () => {
											try { app.summary = await LoadSymbolDetail(app.visibleSymbols[app.symSelected]); } finally { app.isLoading = false; }
										});
										break;
								}
								break;
							case KeyCode.Char when ev.Key.Char == (uint)'o':
								switch (app) {
									// Open in editor from context
                                case { screen: Mode.Browser, visibleSymbols.Count: > 0 }: {
                                    CodeSymbol s = app.visibleSymbols[app.symSelected];
                                    opener.Open(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
                                    break;
                                }
                                case { screen: Mode.Source, visibleSymbols.Count: > 0 }: {
                                    CodeSymbol s = app.visibleSymbols[app.symSelected];
                                    opener.Open(projectPath, s.FilePath, Math.Max(1, app.sourceSelected + 1));
                                    break;
                                }
                                case { screen: Mode.References, refs.Count: > 0 }: {
                                    (string f, int ln, string _) = app.refs[Math.Min(app.refsSelected, app.refs.Count - 1)];
                                    opener.Open(projectPath, f, Math.Max(1, ln));
                                    break;
                                }
								}
								redraw = true;
								break;
							case KeyCode.Esc:
								return;
							case KeyCode.Char:
								if (ev.Key.Char == (uint)'q') return;
								if (ev.Key.Char == (uint)'1') {
									app.screen = Mode.Browser;
									redraw     = true;
									break;
								}
								if (ev.Key.Char == (uint)'2') {
									await EnsureSource(app);
									app.screen = Mode.Source;
									redraw     = true;
									break;
								}
								if (ev.Key.Char == (uint)'3') {
									if (app is { isLoading: false, visibleSymbols.Count: > 0 } && string.IsNullOrEmpty(app.summary)) {
										app.isLoading = true;
										_ = Task.Run(async () => {
											try { app.summary = await LoadSymbolDetail(app.visibleSymbols[app.symSelected]); } finally { app.isLoading = false; }
										});
									}
									app.screen = Mode.Summary;
									redraw     = true;
									break;
								}
								if (ev.Key.Char == (uint)'4') {
									await EnsureRefs(app);
									app.screen = Mode.References;
									redraw     = true;
									break;
								}
								if (ev.Key.Char == (uint)'5') {
									app.screen = Mode.Info;
									redraw     = true;
									break;
								}
								if (ev.Key.Char == (uint)'/') {
									if (app.screen != Mode.Browser) {
										app.screen = Mode.Browser;
										redraw     = true;
										break;
									}
                            if (app.focus == Panel.Files) {
                                app.fileFilter = string.Empty;
                                ApplyFileFilter(app);
                            } else {
                                app.symFilter = string.Empty;
                                ApplySymbolFilter(app);
                            }
									redraw = true;
									break;
								}
								char ch = (char)ev.Key.Char;
								if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/') {
                                if (app.screen != Mode.Browser) {
                                    /* ignore typing in op screens */
                                } else if (app.focus == Panel.Files) {
                                    app.fileFilter += ch;
                                    ApplyFileFilter(app);
                                } else {
                                    app.symFilter += ch;
                                    ApplySymbolFilter(app);
                                }
									redraw = true;
								}
								break;
							case KeyCode.Backspace:
								if (app.screen != Mode.Browser) {
									/* ignore */
								} else
                                switch (app) {
                                    case { focus: Panel.Files, fileFilter.Length: > 0 }:
                                        app.fileFilter = app.fileFilter[..^1];
                                        ApplyFileFilter(app);
                                        redraw = true;
                                        break;
                                    case { focus: Panel.Symbols, symFilter.Length: > 0 }:
                                        app.symFilter = app.symFilter[..^1];
                                        ApplySymbolFilter(app);
                                        redraw = true;
                                        break;
                                }
								break;
						}
						break;
				}
			}
        } finally {
            // no lists to dispose; BrowserScreen constructs per-draw widgets
        }
	}

	private async Task<string> LoadSymbolDetail(CodeSymbol symbol) {
		try {
			string? source = await _crawler.GetCode(symbol);
			if (string.IsNullOrEmpty(source)) return "No source available for symbol.";
			OptimizationContext ctx = new OptimizationContext(Level: symbol.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
				AvailableKeys: [],
				PromptName: GLB.GetDefaultPrompt(symbol));
			string summary = await _compressor.OptimizeSymbolAsync(symbol, ctx, source);
			return summary;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error summarizing symbol {Name}", symbol.Name);
			return $"Error: {ex.Message}";
		}
	}

private static void Draw(Terminal term, string projectPath, AppState app) {
		(int w, int h) = term.Size();
		Rect area = Rect.FromSize(Math.Max(1, w), Math.Max(1, h));

		IReadOnlyList<Rect> rows = Layout.SplitHorizontal(area, [
			Constraint.Length(3),
			Constraint.Percentage(100),
			Constraint.Length(1)
		], gap: 0, margin: 0);

        using Paragraph header = MakeHeader(Path.GetFileName(projectPath), app);
        term.Draw(header, rows[0]);

		switch (app.screen) {
            case Mode.Browser: { new BrowserScreen().Draw(term, rows[1], app, projectPath); break; }
            case Mode.Source: { new SourceScreen().Draw(term, rows[1], app, projectPath); break; }
            case Mode.Summary: { new SummaryScreen().Draw(term, rows[1], app, projectPath); break; }
            case Mode.References: { new ReferencesScreen().Draw(term, rows[1], app, projectPath); break; }
            case Mode.Info: { new InfoScreen().Draw(term, rows[1], app, projectPath); break; }
		}

        using Paragraph footer = MakeFooter(app);
        term.Draw(footer, rows[2]);
    }

    private static Paragraph MakeHeader(string projectName, AppState app) {
        string focusText = app.focus == Panel.Files ? "Files" : "Symbols";
        string modeText  = app.screen switch { Mode.Browser => "Browser", Mode.Source => "Source", Mode.Summary => "Summary", Mode.References => "References", Mode.Info => "Info", _ => "" };
        return new Paragraph($"Thaum — {projectName}  Mode: {modeText}  Focus: {focusText}")
            .AppendLine("1 Browser 2 Source 3 Summary 4 Refs 5 Info    Tab switch    / filter    o open  q quit", TuiTheme.Hint);
    }

    private static Paragraph MakeFooter(AppState app) {
        string text = app.screen switch {
            Mode.Browser    => "↑/↓ move  Tab switch  / filter  Enter open/summarize  2 Source 3 Summary 4 Refs 5 Info",
            Mode.Source     => "↑/↓ scroll  1 Browser 3 Summary 4 Refs 5 Info",
            Mode.Summary    => (app.isLoading ? "Summarizing…" : "") + " 1 Browser 2 Source 4 Refs 5 Info",
            Mode.References => "↑/↓ scroll  1 Browser 2 Source 3 Summary 5 Info",
            Mode.Info       => "1 Browser 2 Source 3 Summary 4 Refs",
            _               => string.Empty
        };
        return new Paragraph(text).AppendSpan("   o open   ", TuiTheme.Hint).AppendSpan("Ratatui.cs", TuiTheme.Hint);
    }

    private static IEnumerable<CodeSymbol> SymbolsForFile(AppState app, string? file)
        => string.IsNullOrEmpty(file) ? app.allSymbols : app.allSymbols.Where(s => s.FilePath == file);

	private static void ApplyFileFilter(AppState app) {
		if (string.IsNullOrWhiteSpace(app.fileFilter)) app.visibleFiles = app.allFiles.ToList();
		else {
			string f = app.fileFilter.ToLowerInvariant();
			app.visibleFiles = app.allFiles.Where(p => p.ToLowerInvariant().Contains(f)).ToList();
		}
		app.fileSelected = 0;
		app.fileOffset   = 0;
		app.summary      = null;
		string? file = app.visibleFiles.FirstOrDefault();
		app.visibleSymbols = file == null ? [] : SymbolsForFile(app, file).ToList();
		app.symSelected    = 0;
		app.symOffset      = 0;
	}

	private static void ApplySymbolFilter(AppState app) {
		string?                 file    = app.visibleFiles.Count == 0 ? null : app.visibleFiles[Math.Min(app.fileSelected, app.visibleFiles.Count - 1)];
		IEnumerable<CodeSymbol> baseSet = SymbolsForFile(app, file);
		if (string.IsNullOrWhiteSpace(app.symFilter)) app.visibleSymbols = baseSet.ToList();
		else {
			string f = app.symFilter.ToLowerInvariant();
			app.visibleSymbols = baseSet.Where(s => s.Name.ToLowerInvariant().Contains(f)).ToList();
		}
		app.symSelected = 0;
		app.symOffset   = 0;
		app.summary     = null;
	}

    // Symbol formatting handled by BrowserScreen

    // StyleForKind moved to TuiTheme

	private async Task EnsureSource(AppState app) {
		if (app.visibleSymbols.Count == 0) {
			app.sourceLines = [];
			return;
		}
		if (app.sourceLines is { Count: > 0 }) return;
		CodeSymbol s   = app.visibleSymbols[app.symSelected];
		string?    src = await _crawler.GetCode(s);
		app.sourceLines    = (src ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
		app.sourceSelected = 0;
		app.sourceOffset   = 0;
	}

	private async Task EnsureRefs(AppState app) {
		if (app.visibleSymbols.Count == 0) {
			app.refs = [];
			return;
		}
		if (app.refs is { Count: > 0 }) return;
		CodeSymbol       s    = app.visibleSymbols[app.symSelected];
		List<CodeSymbol> refs = await _crawler.GetReferencesFor(s.Name, s.StartCodeLoc);
		app.refs         = refs.Select(r => (r.FilePath, r.StartCodeLoc.Line, r.Name)).ToList();
		app.refsSelected = 0;
		app.refsOffset   = 0;
	}

    internal static void EnsureVisible(ref int offset, int selected) {
		if (selected < offset) offset = selected;
		int max                       = offset + 20;
		if (selected >= max) offset   = Math.Max(0, selected - 19);
	}

    // Text wrapping helpers removed; screens render directly

    // Spinner and editor logic moved to TuiTheme and DefaultEditorOpener
}
