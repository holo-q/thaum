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

	private enum Panel { Files, Symbols }

	private enum Mode { Browser, Source, Summary, References, Info }

	private sealed class AppState {
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

	// Global, semantically meaningful colors/styles
	private static class Theme {
		public static readonly Style Hint       = new Style(dim: true);
		public static readonly Style FilePath   = new Style(fg: Color.Cyan);
		public static readonly Style LineNumber = new Style(fg: Color.DarkGray);
		public static readonly Style Error      = new Style(fg: Color.LightRed, bold: true);
		public static readonly Style Success    = new Style(fg: Color.LightGreen, bold: true);
		public static readonly Style Info       = new Style(fg: Color.LightBlue);
        public static readonly Style Title      = new Style(bold: true);
        public static readonly Style CodeHi     = new Style(fg: Color.LightYellow, bold: true);
	}

	public async Task RunAsync(CodeMap codeMap, string projectPath, string language) {
		List<CodeSymbol> allSymbols = codeMap.ToList()
			.OrderBy(s => (s.FilePath, s.StartCodeLoc.Line))
			.ToList();

		if (allSymbols.Count == 0) {
			Console.WriteLine("No symbols to display");
			return;
		}

		using Terminal term = new Terminal().Raw().AltScreen().ShowCursor(false);

		AppState app = new AppState {
			allSymbols = allSymbols,
			allFiles   = allSymbols.Select(s => s.FilePath).Distinct().OrderBy(x => x).ToList(),
			summary    = null,
			isLoading  = false
		};

		app.visibleFiles = app.allFiles.ToList();
		string? firstFile = app.visibleFiles.FirstOrDefault();
		app.visibleSymbols = firstFile == null ? [] : SymbolsForFile(app, firstFile).ToList();

		List?           filesList  = BuildFilesList(app, projectPath);
		List?           symList    = BuildSymbolsList(app);
		using ListState filesState = new ListState().Selected(app.fileSelected).Offset(app.fileOffset);
		using ListState symState   = new ListState().Selected(app.symSelected).Offset(app.symOffset);

		bool     redraw = true;
		TimeSpan poll   = TimeSpan.FromMilliseconds(75);

		try {
			while (true) {
				if (redraw) {
					Draw(term, projectPath, filesList!, filesState, symList!, symState, app);
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
										filesState.Selected(app.fileSelected);
										EnsureVisible(ref app.fileOffset, app.fileSelected);
										filesState.Offset(app.fileOffset);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
										app.symSelected = Math.Min(app.symSelected + 1, app.visibleSymbols.Count - 1);
										symState.Selected(app.symSelected);
										EnsureVisible(ref app.symOffset, app.symSelected);
										symState.Offset(app.symOffset);
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
										filesState.Selected(app.fileSelected);
										EnsureVisible(ref app.fileOffset, app.fileSelected);
										filesState.Offset(app.fileOffset);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
										app.symSelected = Math.Max(app.symSelected - 1, 0);
										symState.Selected(app.symSelected);
										EnsureVisible(ref app.symOffset, app.symSelected);
										symState.Offset(app.symOffset);
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
										filesState.Selected(app.fileSelected);
										EnsureVisible(ref app.fileOffset, app.fileSelected);
										filesState.Offset(app.fileOffset);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
										app.symSelected = Math.Min(app.symSelected + 10, app.visibleSymbols.Count - 1);
										symState.Selected(app.symSelected);
										EnsureVisible(ref app.symOffset, app.symSelected);
										symState.Offset(app.symOffset);
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
										filesState.Selected(app.fileSelected);
										EnsureVisible(ref app.fileOffset, app.fileSelected);
										filesState.Offset(app.fileOffset);
										redraw = true;
										break;
									case { focus: Panel.Symbols, visibleSymbols.Count: > 0 }:
										app.symSelected = Math.Max(app.symSelected - 10, 0);
										symState.Selected(app.symSelected);
										EnsureVisible(ref app.symOffset, app.symSelected);
										symState.Offset(app.symOffset);
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
										symList?.Dispose();
										symList = BuildSymbolsList(app);
										symState.Selected(app.symSelected).Offset(app.symOffset);
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
										OpenInEditor(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line), out string msg, out bool ok);
										break;
									}
									case { screen: Mode.Source, visibleSymbols.Count: > 0 }: {
										CodeSymbol s = app.visibleSymbols[app.symSelected];
										OpenInEditor(projectPath, s.FilePath, Math.Max(1, app.sourceSelected + 1), out string msg, out bool ok);
										break;
									}
									case { screen: Mode.References, refs.Count: > 0 }: {
										(string f, int ln, string _) = app.refs[Math.Min(app.refsSelected, app.refs.Count - 1)];
										OpenInEditor(projectPath, f, Math.Max(1, ln), out string msg, out bool ok);
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
										filesList?.Dispose();
										filesList = BuildFilesList(app, projectPath);
										filesState.Selected(app.fileSelected).Offset(app.fileOffset);
									} else {
										app.symFilter = string.Empty;
										ApplySymbolFilter(app);
										symList?.Dispose();
										symList = BuildSymbolsList(app);
										symState.Selected(app.symSelected).Offset(app.symOffset);
									}
									redraw = true;
									break;
								}
								char ch = (char)ev.Key.Char;
								if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/') {
									if (app.screen != Mode.Browser) { /* ignore typing in op screens */
									} else if (app.focus == Panel.Files) {
										app.fileFilter += ch;
										ApplyFileFilter(app);
										filesList?.Dispose();
										filesList = BuildFilesList(app, projectPath);
										filesState.Selected(app.fileSelected).Offset(app.fileOffset);
									} else {
										app.symFilter += ch;
										ApplySymbolFilter(app);
										symList?.Dispose();
										symList = BuildSymbolsList(app);
										symState.Selected(app.symSelected).Offset(app.symOffset);
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
											filesList?.Dispose();
											filesList = BuildFilesList(app, projectPath);
											filesState.Selected(app.fileSelected).Offset(app.fileOffset);
											redraw = true;
											break;
										case { focus: Panel.Symbols, symFilter.Length: > 0 }:
											app.symFilter = app.symFilter[..^1];
											ApplySymbolFilter(app);
											symList?.Dispose();
											symList = BuildSymbolsList(app);
											symState.Selected(app.symSelected).Offset(app.symOffset);
											redraw = true;
											break;
									}
								break;
						}
						break;
				}
			}
		} finally {
			filesList?.Dispose();
			symList?.Dispose();
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

	private static void Draw(Terminal term, string projectPath, List filesList, ListState filesState, List symList, ListState symState, AppState app) {
		(int w, int h) = term.Size();
		Rect area = Rect.FromSize(Math.Max(1, w), Math.Max(1, h));

		IReadOnlyList<Rect> rows = Layout.SplitHorizontal(area, [
			Constraint.Length(3),
			Constraint.Percentage(100),
			Constraint.Length(1)
		], gap: 0, margin: 0);

		string focusText = app.focus == Panel.Files ? "Files" : "Symbols";
		string modeText  = app.screen switch { Mode.Browser => "Browser", Mode.Source => "Source", Mode.Summary => "Summary", Mode.References => "References", Mode.Info => "Info", _ => "" };
		using Paragraph header = new Paragraph($"Thaum — {Path.GetFileName(projectPath)}  Mode: {modeText}  Focus: {focusText}")
			.AppendLine("1 Browser 2 Source 3 Summary 4 Refs 5 Info    Tab switch    / filter    o open  q quit", Theme.Hint);
		term.Draw(header, rows[0]);

		switch (app.screen) {
			case Mode.Browser: {
				IReadOnlyList<Rect> cols = Layout.SplitVertical(rows[1], [
					Constraint.Percentage(30),
					Constraint.Percentage(30),
					Constraint.Percentage(40)
				], gap: 1, margin: 0);

				using (Paragraph title = new Paragraph(app.fileFilter.Length == 0 ? "(type to filter)" : $"/{app.fileFilter}").Title("Files", border: true)) { term.Draw(title, new Rect(cols[0].X, cols[0].Y, cols[0].Width, 2)); }
				term.Draw(filesList, new Rect(cols[0].X, cols[0].Y + 2, cols[0].Width, cols[0].Height - 2), filesState);

				using (Paragraph title = new Paragraph(app.symFilter.Length == 0 ? "(type to filter)" : $"/{app.symFilter}").Title("Symbols", border: true)) { term.Draw(title, new Rect(cols[1].X, cols[1].Y, cols[1].Width, 2)); }
				term.Draw(symList, new Rect(cols[1].X, cols[1].Y + 2, cols[1].Width, cols[1].Height - 2), symState);

				bool hasSym = app.visibleSymbols.Count > 0;
				using Paragraph meta = new Paragraph("").Title("Details", border: true)
					.AppendLine(!hasSym ? "" : $"Name: {app.visibleSymbols[app.symSelected].Name}")
					.AppendLine(!hasSym ? "" : $"Kind: {app.visibleSymbols[app.symSelected].Kind}")
					.AppendLine(!hasSym ? "" : $"File: {app.visibleSymbols[app.symSelected].FilePath}")
					.AppendLine(!hasSym ? "" : $"Span: L{app.visibleSymbols[app.symSelected].StartCodeLoc.Line}:C{app.visibleSymbols[app.symSelected].StartCodeLoc.Character}");

				Rect right      = cols[2];
				int  metaHeight = Math.Max(6, right.Height / 3);
				term.Draw(meta, new Rect(right.X, right.Y, right.Width, metaHeight));
                string          body   = app.isLoading ? $"Summarizing… {Spinner()}" : (app.summary ?? "");
                using Paragraph detail = new Paragraph("").Title("Summary", border: true);
                if (body.StartsWith("Error:")) detail.AppendSpan(body, Theme.Error); else detail.AppendSpan(body);
                term.Draw(detail, new Rect(right.X, right.Y + metaHeight + 1, right.Width, Math.Max(1, right.Height - metaHeight - 1)));
                break;
            }
            case Mode.Source: {
                using Paragraph title = new Paragraph("Source").Title("Source", border: true);
                term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
                List<string> lines = app.sourceLines ?? [];
                using List   list  = new List();
                int          start = Math.Max(0, app.sourceOffset);
                int          end   = Math.Min(lines.Count, start + Math.Max(1, rows[1].Height - 3));
                int symStartLine = 0, symEndLine = -1, symStartCol = 0, symEndCol = 0;
                if (app.visibleSymbols.Count > 0) {
                    CodeSymbol s = app.visibleSymbols[app.symSelected];
                    symStartLine = Math.Max(1, s.StartCodeLoc.Line);
                    symEndLine   = Math.Max(symStartLine, s.EndCodeLoc.Line);
                    symStartCol  = Math.Max(0, s.StartCodeLoc.Character);
                    symEndCol    = Math.Max(symStartCol, s.EndCodeLoc.Character);
                }
                for (int i = start; i < end; i++) {
                    string num = (i + 1).ToString().PadLeft(5) + "  ";
                    Memory<byte> ln = Encoding.UTF8.GetBytes(num).AsMemory();
                    string line = lines[i];
                    var runs = new List<Ratatui.Batching.SpanRun>(4) { new Ratatui.Batching.SpanRun(ln, Theme.LineNumber) };
                    int oneBased = i + 1;
                    if (oneBased >= symStartLine && oneBased <= symEndLine) {
                        int sc = (oneBased == symStartLine) ? symStartCol : 0;
                        int ec = (oneBased == symEndLine) ? symEndCol : line.Length;
                        sc = Math.Clamp(sc, 0, line.Length);
                        ec = Math.Clamp(ec, sc, line.Length);
                        string pre = line[..sc], mid = line[sc..ec], post = line[ec..];
                        if (pre.Length > 0) runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(pre).AsMemory(), default));
                        if (mid.Length > 0) runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(mid).AsMemory(), Theme.CodeHi));
                        if (post.Length > 0) runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(post).AsMemory(), default));
                    } else {
                        runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(line).AsMemory(), default));
                    }
                    list.AppendItem(CollectionsMarshal.AsSpan(runs));
                }
                term.Draw(list, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
                break;
            }
            case Mode.Summary: {
                using Paragraph title = new Paragraph("Summary").Title("Summary", border: true);
                term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
                string          body = app.isLoading ? $"Summarizing… {Spinner()}" : (app.summary ?? "No summary yet. Press 3 to (re)generate.");
                using Paragraph para = new Paragraph("");
                if (body.StartsWith("Error:")) para.AppendSpan(body, Theme.Error); else para.AppendSpan(body);
                term.Draw(para, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
                break;
            }
            case Mode.References: {
                using Paragraph title = new Paragraph("References").Title("References", border: true);
                term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
                using List                                 list  = new List();
                List<(string File, int Line, string Name)> refs  = app.refs ?? [];
                int                                        start = Math.Max(0, app.refsOffset);
                int                                        end   = Math.Min(refs.Count, start + Math.Max(1, rows[1].Height - 2));
                for (int i = start; i < end; i++) {
                    (string f, int ln, string nm) = refs[i];
                    Memory<byte> lhs = Encoding.UTF8.GetBytes($"{Path.GetFileName(f)}:{ln}  ").AsMemory();
                    Memory<byte> rhs = Encoding.UTF8.GetBytes(nm).AsMemory();
                    ReadOnlyMemory<Ratatui.Batching.SpanRun> runs = new[] {
                        new Ratatui.Batching.SpanRun(lhs, Theme.FilePath),
                        new Ratatui.Batching.SpanRun(rhs, default)
                    };
                    list.AppendItem(runs.Span);
                }
                term.Draw(list, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
                break;
            }
            case Mode.Info: {
                using Paragraph title = new Paragraph("Info").Title("Info", border: true);
                term.Draw(title, new Rect(rows[1].X, rows[1].Y, rows[1].Width, 2));
                if (app.visibleSymbols.Count > 0) {
                    CodeSymbol s = app.visibleSymbols[app.symSelected];
                    using Paragraph para = new Paragraph("");
                    para.AppendSpan("Name: ", Theme.Hint).AppendSpan(s.Name, StyleForKind(s.Kind)).AppendLine("");
                    para.AppendSpan("Kind: ", Theme.Hint).AppendSpan(s.Kind.ToString(), Theme.Info).AppendLine("");
                    para.AppendSpan("File: ", Theme.Hint).AppendSpan(s.FilePath, Theme.FilePath).AppendLine("");
                    para.AppendSpan("Start: ", Theme.Hint).AppendSpan($"L{s.StartCodeLoc.Line}", Theme.LineNumber).AppendSpan(":", Theme.Hint).AppendSpan($"C{s.StartCodeLoc.Character}", Theme.LineNumber).AppendLine("");
                    para.AppendSpan("End:   ", Theme.Hint).AppendSpan($"L{s.EndCodeLoc.Line}", Theme.LineNumber).AppendSpan(":", Theme.Hint).AppendSpan($"C{s.EndCodeLoc.Character}", Theme.LineNumber).AppendLine("");
                    para.AppendSpan("Children: ", Theme.Hint).AppendSpan((s.Children?.Count ?? 0).ToString(), Theme.Info).AppendLine("");
                    para.AppendSpan("Deps: ", Theme.Hint).AppendSpan((s.Dependencies?.Count ?? 0).ToString(), Theme.Info).AppendLine("");
                    para.AppendSpan("Last: ", Theme.Hint).AppendSpan((s.LastModified?.ToString("u") ?? "n/a"), Theme.Info);
                    term.Draw(para, new Rect(rows[1].X, rows[1].Y + 2, rows[1].Width, rows[1].Height - 2));
                }
                break;
            }
		}

		using Paragraph footer = new Paragraph(app.screen switch {
				Mode.Browser    => "↑/↓ move  Tab switch  / filter  Enter open/summarize  2 Source 3 Summary 4 Refs 5 Info",
				Mode.Source     => "↑/↓ scroll  1 Browser 3 Summary 4 Refs 5 Info",
				Mode.Summary    => (app.isLoading ? "Summarizing…" : "") + " 1 Browser 2 Source 4 Refs 5 Info",
				Mode.References => "↑/↓ scroll  1 Browser 2 Source 3 Summary 5 Info",
				Mode.Info       => "1 Browser 2 Source 3 Summary 4 Refs",
				_               => ""
			}).AppendSpan("   o open   ", Theme.Hint)
			.AppendSpan("Ratatui.cs", Theme.Hint);
		term.Draw(footer, rows[2]);
	}

	private static IEnumerable<CodeSymbol> SymbolsForFile(AppState app, string? file)
		=> string.IsNullOrEmpty(file) ? app.allSymbols : app.allSymbols.Where(s => s.FilePath == file);

	private static string FileLine(string projectPath, string path) {
		try { return Path.GetRelativePath(projectPath, path); } catch { return Path.GetFileName(path); }
	}

	private static List BuildFilesList(AppState app, string projectPath) {
		List list = new List().Title("Files").HighlightSymbol("→ ").HighlightStyle(new Style(bold: true));
		foreach (string f in app.visibleFiles) list.AppendItem(FileLine(projectPath, f));
		return list;
	}

	private static List BuildSymbolsList(AppState app) {
		List list = new List().Title("Symbols").HighlightSymbol("→ ").HighlightStyle(new Style(bold: true));
		foreach (CodeSymbol s in app.visibleSymbols) list.AppendItem(SymbolLine(s), StyleForKind(s.Kind));
		return list;
	}

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

	private static string SymbolLine(CodeSymbol s) {
		string name = s.Name.Replace('\n', ' ');
		return $"{Icon(s)} {name}";
	}

	private static string Icon(CodeSymbol s) => s.Kind switch {
		SymbolKind.Class     => "[C]",
		SymbolKind.Method    => "[M]",
		SymbolKind.Function  => "[F]",
		SymbolKind.Interface => "[I]",
		SymbolKind.Enum      => "[E]",
		_                    => "[·]"
	};

	private static Style StyleForKind(SymbolKind k) => k switch {
		SymbolKind.Class     => new Style(fg: Color.LightYellow),
		SymbolKind.Method    => new Style(fg: Color.LightGreen),
		SymbolKind.Function  => new Style(fg: Color.LightGreen),
		SymbolKind.Interface => new Style(fg: Color.LightBlue),
		SymbolKind.Enum      => new Style(fg: Color.Magenta),
		SymbolKind.Property  => new Style(fg: Color.White),
		SymbolKind.Field     => new Style(fg: Color.White),
		SymbolKind.Variable  => new Style(fg: Color.White),
		_                    => new Style(fg: Color.Gray)
	};

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

	private static void EnsureVisible(ref int offset, int selected) {
		if (selected < offset) offset = selected;
		int max                       = offset + 20;
		if (selected >= max) offset   = Math.Max(0, selected - 19);
	}

	private static string Truncate(string s, int width) {
		if (width <= 1) return s;
		return s.Length <= width ? s : s[..Math.Max(0, width - 1)] + "…";
	}

	private static List<string> Wrap(string s, int width) {
		if (width < 5) return [Truncate(s, width)];
		string[]     words = s.Replace("\r", string.Empty).Split('\n');
		List<string> lines = [];
		foreach (string para in words) {
			string[]      parts = para.Split(' ');
			StringBuilder line  = new StringBuilder();
			foreach (string p in parts) {
				if (line.Length + p.Length + 1 > width) {
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

	private static string Spinner() {
		int t = (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 120) % 4);
		return "-\\|/"[t].ToString();
	}

        private static void OpenInEditor(string projectPath, string filePath, int line, out string message, out bool success) {
            success = false;
            message = string.Empty;
            try {
                string full = Path.IsPathRooted(filePath) ? filePath : Path.Combine(projectPath, filePath);
                string? cmd = Environment.GetEnvironmentVariable("THAUM_EDITOR") ?? Environment.GetEnvironmentVariable("EDITOR");
                string args = string.Empty;

                bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                bool isMac     = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

                if (string.IsNullOrWhiteSpace(cmd)) {
                    if (isWindows) {
                        cmd  = "code"; // VS Code
                        args = $"-g \"{full}\":{line}";
                    } else if (isMac) {
                        cmd  = "open";
                        args = $"-a \"Visual Studio Code\" --args -g \"{full}\":{line}";
                    } else {
                        if (File.Exists("/usr/bin/code") || File.Exists("/usr/local/bin/code")) { cmd = "code"; args = $"-g \"{full}\":{line}"; }
                        else if (File.Exists("/usr/bin/nvim") || File.Exists("/usr/local/bin/nvim")) { cmd = "nvim"; args = $"+{line} \"{full}\""; }
                        else if (File.Exists("/usr/bin/vim") || File.Exists("/usr/local/bin/vim")) { cmd = "vim"; args = $"+{line} \"{full}\""; }
                        else { message = $"Open: {full}:{line}"; return; }
                    }
                } else {
                    string c = cmd.ToLowerInvariant();
                    if (c.Contains("code")) args = $"-g \"{full}\":{line}";
                    else if (c.Contains("nvim") || c.Contains("vim")) args = $"+{line} \"{full}\"";
                    else if (isMac && c == "open") args = $"\"{full}\"";
                    else args = $"\"{full}\"";
                }

                ProcessStartInfo psi = new ProcessStartInfo(cmd, args) { UseShellExecute = false };
                Process.Start(psi);
                success = true;
                message = $"Opened {Path.GetFileName(full)}:{line}";
            } catch (Exception ex) {
                message = $"Open failed: {ex.Message}";
            }
        }
}
