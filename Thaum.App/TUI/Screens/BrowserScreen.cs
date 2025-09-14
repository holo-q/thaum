using Ratatui;
using Ratatui.Layout;
using static Thaum.App.RatatuiTUI.RatLayout;
using static Thaum.App.RatatuiTUI.Rat;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

internal sealed class BrowserScreen : Screen {
	private bool _keysReady;

	public BrowserScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
		IReadOnlyList<Rect> cols = V(area, [
			Constraint.Percentage(30),
			Constraint.Percentage(30),
			Constraint.Percentage(40)
		], gap: 1, margin: 0);

		using (Paragraph title = Paragraph(app.fileFilter.Length == 0 ? "(type to filter)" : $"/{app.fileFilter}", title: "Files", title_border: true))
			term.Draw(title, R(cols[0].X, cols[0].Y, cols[0].Width, 2));

		using (List files = List("Files")) {
			foreach (string f in app.visibleFiles)
				files.AppendItem(FileLine(projectPath, f), TuiTheme.FilePath);
			using ListState fs = ListState(selected: app.fileSelected, offset: app.fileOffset);
			term.Draw(files, R(cols[0].X, cols[0].Y + 2, cols[0].Width, cols[0].Height - 2), fs);
		}

		using (Paragraph title = Paragraph(app.symFilter.Length == 0 ? "(type to filter)" : $"/{app.symFilter}", title: "Symbols", title_border: true))
			term.Draw(title, R(cols[1].X, cols[1].Y, cols[1].Width, 2));

		using (List syms = List("Symbols")) {
			foreach (CodeSymbol s in app.visibleSymbols)
				syms.AppendItem(SymbolLine(s), TuiTheme.StyleForKind(s.Kind));
			using ListState ss = ListState(selected: app.symSelected, offset: app.symOffset);
			term.Draw(syms, R(cols[1].X, cols[1].Y + 2, cols[1].Width, cols[1].Height - 2), ss);
		}

		bool hasSym = app.visibleSymbols.Count > 0;
		using Paragraph meta = Paragraph("", title: "Details", title_border: true)
			.AppendLine(!hasSym ? "" : $"Name: {app.visibleSymbols[app.symSelected].Name}")
			.AppendLine(!hasSym ? "" : $"Kind: {app.visibleSymbols[app.symSelected].Kind}")
			.AppendLine(!hasSym ? "" : $"File: {app.visibleSymbols[app.symSelected].FilePath}")
			.AppendLine(!hasSym ? "" : $"Span: L{app.visibleSymbols[app.symSelected].StartCodeLoc.Line}:C{app.visibleSymbols[app.symSelected].StartCodeLoc.Character}");

		Rect right      = cols[2];
		int  metaHeight = Math.Max(6, right.Height / 3);
		term.Draw(meta, R(right.X, right.Y, right.Width, metaHeight));
		string          body   = app.isLoading ? $"Summarizing… {TuiTheme.Spinner()}" : (app.summary ?? "");
		using Paragraph detail = Paragraph("", title: "Summary", title_border: true);
		if (body.StartsWith("Error:")) detail.AppendSpan(body, TuiTheme.Error);
		else detail.AppendSpan(body);
		term.Draw(detail, R(right.X, right.Y + metaHeight + 1, right.Width, Math.Max(1, right.Height - metaHeight - 1)));
	}

	public override string FooterHint(ThaumTUI.State app)
		=> "↑/↓ move  Tab switch  / filter  Enter open/summarize  o open";

	public override string Title(ThaumTUI.State app)
		=> $"Browser — Focus: {(app.focus == ThaumTUI.Panel.Files ? "Files" : "Symbols")}";

	// Key help comes from KeybindManager registrations

	public override bool HandleKey(Event ev, ThaumTUI.State app) {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
		}

		if (keys.Handle(ev, app)) return true;
		if (ev.Kind != EventKind.Key) return false;

		switch (ev.Key.CodeEnum) {
			case KeyCode.Down:
				switch (app) {
					case { focus: ThaumTUI.Panel.Files, visibleFiles.Count: > 0 }:
						app.fileSelected = Math.Min(app.fileSelected + 1, app.visibleFiles.Count - 1);
						tui.EnsureVisible(ref app.fileOffset, app.fileSelected);
						return true;
					case { focus: ThaumTUI.Panel.Symbols, visibleSymbols.Count: > 0 }:
						app.symSelected = Math.Min(app.symSelected + 1, app.visibleSymbols.Count - 1);
						tui.EnsureVisible(ref app.symOffset, app.symSelected);
						app.summary = null;
						return true;
					default:
						return false;
				}
			case KeyCode.Up:
				switch (app) {
					case { focus: ThaumTUI.Panel.Files, visibleFiles.Count: > 0 }:
						app.fileSelected = Math.Max(app.fileSelected - 1, 0);
						tui.EnsureVisible(ref app.fileOffset, app.fileSelected);
						return true;
					case { focus: ThaumTUI.Panel.Symbols, visibleSymbols.Count: > 0 }:
						app.symSelected = Math.Max(app.symSelected - 1, 0);
						tui.EnsureVisible(ref app.symOffset, app.symSelected);
						app.summary = null;
						return true;
					default:
						return false;
				}
			case KeyCode.PageDown:
				switch (app) {
					case { focus: ThaumTUI.Panel.Files, visibleFiles.Count: > 0 }:
						app.fileSelected = Math.Min(app.fileSelected + 10, app.visibleFiles.Count - 1);
						tui.EnsureVisible(ref app.fileOffset, app.fileSelected);
						return true;
					case { focus: ThaumTUI.Panel.Symbols, visibleSymbols.Count: > 0 }:
						app.symSelected = Math.Min(app.symSelected + 10, app.visibleSymbols.Count - 1);
						tui.EnsureVisible(ref app.symOffset, app.symSelected);
						app.summary = null;
						return true;
					default:
						return false;
				}
			case KeyCode.PageUp:
				switch (app) {
					case { focus: ThaumTUI.Panel.Files, visibleFiles.Count: > 0 }:
						app.fileSelected = Math.Max(app.fileSelected - 10, 0);
						tui.EnsureVisible(ref app.fileOffset, app.fileSelected);
						return true;
					case { focus: ThaumTUI.Panel.Symbols, visibleSymbols.Count: > 0 }:
						app.symSelected = Math.Max(app.symSelected - 10, 0);
						tui.EnsureVisible(ref app.symOffset, app.symSelected);
						app.summary = null;
						return true;
					default:
						return false;
				}
			case KeyCode.Tab:
				app.focus = app.focus == ThaumTUI.Panel.Files ? ThaumTUI.Panel.Symbols : ThaumTUI.Panel.Files;
				return true;
			case KeyCode.Enter:
			case KeyCode.Right:
				switch (app) {
					case { focus: ThaumTUI.Panel.Files, visibleFiles.Count: > 0 }: {
						string file = app.visibleFiles[app.fileSelected];
						app.visibleSymbols = app.allSymbols.Where(s => s.FilePath == file).ToList();
						app.symSelected    = 0;
						app.symOffset      = 0;
						app.summary        = null;
						return true;
					}
					case { focus: ThaumTUI.Panel.Symbols, visibleSymbols.Count: > 0, isLoading: false }:
						StartTask(async _ => {
							app.isLoading = true;
							try { app.summary = await tui.LoadSymbolDetail(app.visibleSymbols[app.symSelected]); } finally { app.isLoading = false; }
						});
						return true;
					default:
						return false;
				}
			case KeyCode.Char when ev.Key.Char == 'o':
				if (app is { visibleSymbols.Count: > 0 }) {
					CodeSymbol s = app.visibleSymbols[app.symSelected];
					opener.Open(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
					return true;
				}
				return false;
			case KeyCode.Char when ev.Key.Char == '/':
				if (app.focus == ThaumTUI.Panel.Files) {
					app.fileFilter = string.Empty;
					ApplyFileFilter(app);
				} else {
					app.symFilter = string.Empty;
					ApplySymbolFilter(app);
				}
				return true;
			case KeyCode.Char:
				char ch = (char)ev.Key.Char;
				if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/') {
					if (app.focus == ThaumTUI.Panel.Files) {
						app.fileFilter += ch;
						ApplyFileFilter(app);
					} else {
						app.symFilter += ch;
						ApplySymbolFilter(app);
					}
					return true;
				}
				break;
			case KeyCode.Backspace:
				switch (app) {
					case { focus: ThaumTUI.Panel.Files, fileFilter.Length: > 0 }:
						app.fileFilter = app.fileFilter[..^1];
						ApplyFileFilter(app);
						return true;
					case { focus: ThaumTUI.Panel.Symbols, symFilter.Length: > 0 }:
						app.symFilter = app.symFilter[..^1];
						ApplySymbolFilter(app);
						return true;
					default:
						return false;
				}
		}
		return base.HandleKey(ev, app);
	}

	private void ConfigureKeys() {
		static bool IsFilterChar(char ch) => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/';

		ConfigureDefaultGlobalKeys();
		keys
			.RegisterKey(KeyCode.Down, "↓", "move", KEY_Down)
			.RegisterKey(KeyCode.Up, "↑", "move", KEY_Up)
			.RegisterKey(KeyCode.PageDown, "PgDn", "jump", KEY_JumpDown)
			.RegisterKey(KeyCode.PageUp, "PgUp", "jump", KEY_JumpUp)
			.RegisterKey(KeyCode.Tab, "Tab", "switch focus", KEY_Tab)
			.RegisterKey(KeyCode.Enter, "Enter", "open/summarize", KEY_Enter)
			.RegisterKey(KeyCode.Right, "→", "open/summarize", KEY_Enter)
			.RegisterChar('o', "open in editor", KEY_OpenInEditor)
			.RegisterChar('/', "filter", KEY_Filter)
			.Register("char",
				"type filter",
				ev => ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char } && IsFilterChar((char)ev.Key.Char),
				(ev, a) => {
					if (a.focus == ThaumTUI.Panel.Files) {
						a.fileFilter += (char)ev.Key.Char;
						ApplyFileFilter(a);
					} else {
						a.symFilter += (char)ev.Key.Char;
						ApplySymbolFilter(a);
					}
					return true;
				})
			.RegisterKey(KeyCode.Backspace, "Backspace", "erase", Handler4);
		return;
	}

	private bool KEY_Tab(ThaumTUI.State a) {
		a.focus = a.focus == ThaumTUI.Panel.Files ? ThaumTUI.Panel.Symbols : ThaumTUI.Panel.Files;
		return true;
	}

	private bool Handler4(ThaumTUI.State a) {
		switch (a.focus) {
			case ThaumTUI.Panel.Files when a.fileFilter.Length > 0:
				a.fileFilter = a.fileFilter[..^1];
				ApplyFileFilter(a);
				return true;
			case ThaumTUI.Panel.Symbols when a.symFilter.Length > 0:
				a.symFilter = a.symFilter[..^1];
				ApplySymbolFilter(a);
				return true;
			default:
				return false;
		}
	}

	private bool KEY_Filter(ThaumTUI.State a) {
		if (a.focus == ThaumTUI.Panel.Files) {
			a.fileFilter = string.Empty;
			ApplyFileFilter(a);
		} else {
			a.symFilter = string.Empty;
			ApplySymbolFilter(a);
		}
		return true;
	}

	private bool KEY_OpenInEditor(ThaumTUI.State a) {
		if (a.visibleSymbols.Count > 0) {
			CodeSymbol s = a.visibleSymbols[a.symSelected];
			opener.Open(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
			return true;
		}
		return false;
	}

	private bool KEY_Enter(ThaumTUI.State a) {
		switch (a.focus) {
			case ThaumTUI.Panel.Files when a.visibleFiles.Count > 0: {
				string file = a.visibleFiles[a.fileSelected];
				a.visibleSymbols = a.allSymbols.Where(s => s.FilePath == file).ToList();
				a.symSelected    = 0;
				a.symOffset      = 0;
				a.summary        = null;
				return true;
			}
			case ThaumTUI.Panel.Symbols when a.visibleSymbols.Count > 0 && !a.isLoading:
				StartTask(async _ => {
					a.isLoading = true;
					try {
						a.summary = await tui.LoadSymbolDetail(a.visibleSymbols[a.symSelected]);
					} finally {
						a.isLoading = false;
					}
				});
				return true;
			default:
				return false;
		}
	}


	private bool KEY_JumpUp(ThaumTUI.State a) {
		switch (a.focus) {
			case ThaumTUI.Panel.Files when a.visibleFiles.Count > 0:
				a.fileSelected = Math.Max(a.fileSelected - 10, 0);
				tui.EnsureVisible(ref a.fileOffset, a.fileSelected);
				return true;
			case ThaumTUI.Panel.Symbols when a.visibleSymbols.Count > 0:
				a.symSelected = Math.Max(a.symSelected - 10, 0);
				tui.EnsureVisible(ref a.symOffset, a.symSelected);
				a.summary = null;
				return true;
			default:
				return false;
		}
	}

	private bool KEY_JumpDown(ThaumTUI.State a) {
		switch (a.focus) {
			case ThaumTUI.Panel.Files when a.visibleFiles.Count > 0:
				a.fileSelected = Math.Min(a.fileSelected + 10, a.visibleFiles.Count - 1);
				tui.EnsureVisible(ref a.fileOffset, a.fileSelected);
				return true;
			case ThaumTUI.Panel.Symbols when a.visibleSymbols.Count > 0:
				a.symSelected = Math.Min(a.symSelected + 10, a.visibleSymbols.Count - 1);
				tui.EnsureVisible(ref a.symOffset, a.symSelected);
				a.summary = null;
				return true;
			default:
				return false;
		}
	}

	private bool KEY_Up(ThaumTUI.State a) {
		if (a.visibleFiles.Count > 0 && a.focus == ThaumTUI.Panel.Files) {
			a.fileSelected = Math.Max(a.fileSelected - 1, 0);
			tui.EnsureVisible(ref a.fileOffset, a.fileSelected);
			return true;
		}
		if (a.visibleSymbols.Count > 0 && a.focus == ThaumTUI.Panel.Symbols) {
			a.symSelected = Math.Max(a.symSelected - 1, 0);
			tui.EnsureVisible(ref a.symOffset, a.symSelected);
			a.summary = null;
			return true;
		}
		return false;
	}

	private bool KEY_Down(ThaumTUI.State a) {
		if (a.visibleFiles.Count > 0 && a.focus == ThaumTUI.Panel.Files) {
			a.fileSelected = Math.Min(a.fileSelected + 1, a.visibleFiles.Count - 1);
			tui.EnsureVisible(ref a.fileOffset, a.fileSelected);
			return true;
		}
		if (a.visibleSymbols.Count > 0 && a.focus == ThaumTUI.Panel.Symbols) {
			a.symSelected = Math.Min(a.symSelected + 1, a.visibleSymbols.Count - 1);
			tui.EnsureVisible(ref a.symOffset, a.symSelected);
			a.summary = null;
			return true;
		}
		return false;
	}

	private static string FileLine(string projectPath, string path) {
		try {
			return Path.GetRelativePath(projectPath, path);
		} catch {
			return Path.GetFileName(path);
		}
	}

	private static string SymbolLine(CodeSymbol s)
		=> $"{(s.Kind switch { SymbolKind.Class => "[C]", SymbolKind.Method => "[M]", SymbolKind.Function => "[F]", SymbolKind.Interface => "[I]", SymbolKind.Enum => "[E]", _ => "[·]" })} {s.Name.Replace('\n', ' ')}";

	private static void ApplyFileFilter(ThaumTUI.State app) {
		app.visibleFiles = string.IsNullOrWhiteSpace(app.fileFilter)
			? app.allFiles.ToList()
			: app.allFiles.Where(p => p.ToLowerInvariant().Contains(app.fileFilter.ToLowerInvariant())).ToList();

		app.fileSelected = 0;
		app.fileOffset   = 0;
		app.summary      = null;
		string? file = app.visibleFiles.FirstOrDefault();
		app.visibleSymbols = file == null ? [] : app.allSymbols.Where(s => s.FilePath == file).ToList();
		app.symSelected    = 0;
		app.symOffset      = 0;
	}

	private static void ApplySymbolFilter(ThaumTUI.State app) {
		string?                 file    = app.visibleFiles.Count == 0 ? null : app.visibleFiles[Math.Min(app.fileSelected, app.visibleFiles.Count - 1)];
		IEnumerable<CodeSymbol> baseSet = string.IsNullOrEmpty(file) ? app.allSymbols : app.allSymbols.Where(s => s.FilePath == file);
		app.visibleSymbols = string.IsNullOrWhiteSpace(app.symFilter)
			? baseSet.ToList()
			: baseSet.Where(s => s.Name.ToLowerInvariant().Contains(app.symFilter.ToLowerInvariant())).ToList();
		app.symSelected = 0;
		app.symOffset   = 0;
		app.summary     = null;
	}
}