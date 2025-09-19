using System.Diagnostics.CodeAnalysis;
using Ratatui;
using Ratatui.Layout;
using Thaum.Core.Crawling;
using static Thaum.App.RatatuiTUI.RatLayout;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.Styles;

namespace Thaum.App.RatatuiTUI;

/// <summary>
/// Presents the browser view with file and symbol lists alongside symbol metadata.
/// </summary>
public sealed class BrowserScreen : Screen {
	private bool _keysReady;

	public BrowserScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override string FooterHint(ThaumTUI.State app)
		=> "↑/↓ move  Tab switch  / filter  Enter open/summarize  o open";

	public override string Title(ThaumTUI.State app)
		=> $"Browser — Focus: {(app.focus == ThaumTUI.Panel.Files ? "Files" : "Symbols")}";

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
		bool hasSym     = app.visibleSymbols.Count > 0;
		bool filesFocus = app.focus == ThaumTUI.Panel.Files;
		bool symsFocus  = app.focus == ThaumTUI.Panel.Symbols;

		IReadOnlyList<Rect> cols = SplitV(area, [
			Constraint.Percentage(30),
			Constraint.Percentage(30),
			Constraint.Percentage(40)
		], gap: 1, margin: 0);

		string t1 = app.fileFilter.Length == 0 ? "(/ to filter)" : $"/{app.fileFilter}";
		string t2 = app.symFilter.Length == 0 ? "(type to filter)" : $"/{app.symFilter}";

		Rect      r1        = cols[0];
		Paragraph r1title   = Paragraph(t1, title: "Files", title_border: true);
		Rect      r1inner   = r1.Inner();
		Rect      r1content = r1inner.Body();

		Rect      r2      = cols[1];
		Paragraph r2title = Paragraph(t2, title: "Symbols", title_border: true);
		Rect      r2inner = r2.Inner();
		(Rect r2header, Rect r2content) = r2inner.SplitTop(1);

		Rect r3 = cols[2];
		(Rect rDetails, Rect rSummary) = r3.SplitTop(Math.Max(6, r3.Height / 3));

		int filesView = Math.Max(1, r1content.Height);
		int symsView  = Math.Max(1, r2content.Height);

		// FILES
		// full-height border with title; draw content inside inner rect
		// ----------------------------------------
		term.Draw(r1title, r1);

		Paragraph lbFiles = Paragraph("", title: filesFocus ? " Files " : " files ");
		// lbFiles += (filesFocus ? S_TITLE_ACTIVE : S_TITLE_DIM) |
		//            (filesFocus ? " Files " : " files ");
		// term.Draw(lbFiles, r1inner.WithMinSize(1, 1));

		app.visibleFiles.Draw(term, r1content, 0, FileLine, _ => S_PATH);

		// SYMBOLS
		// full-height border with title; draw content inside inner rect
		// ----------------------------------------
		term.Draw(r2title, r2);

		Paragraph pSymbols = Paragraph("", title: symsFocus ? " Symbols " : " symbols ");
		pSymbols += (symsFocus ? " Symbols " : " symbols ") |
		            (symsFocus ? S_TITLE_ACTIVE : S_TITLE_DIM);
		// term.Draw(pSymbols, r2header.WithMinSize(minWidth: 1, minHeight: 1));

		app.visibleSymbols.Draw(term, r2content, 0, SymbolLine, s => ThaumStyles.StyleForKind(s.Kind));

		term.Draw(Paragraph("", "Details", true)
				.AppendLine(!hasSym ? "" : $"Name: {app.visibleSymbols.Selected.Name}")
				.AppendLine(!hasSym ? "" : $"Kind: {app.visibleSymbols.Selected.Kind}")
				.AppendLine(!hasSym ? "" : $"File: {app.visibleSymbols.Selected.FilePath}")
				.AppendLine(!hasSym ? "" : $"Span: L{app.visibleSymbols.Selected.StartCodeLoc.Line}:C{app.visibleSymbols.Selected.StartCodeLoc.Character}"),
			rDetails);

		string body = app.isLoading
			? $"Summarizing… {Spinner()}"
			: app.summary ?? "";

		Paragraph detail = Paragraph("", title: "Summary", title_border: true);
		if (body.StartsWith("Error:"))
			detail += body | S_ERROR;
		else
			detail += body;

		term.Draw(detail, rSummary.WithMinSize(minHeight: 1));
	}


	// Key help comes from KeybindManager registrations
	public override Task OnEnter(ThaumTUI.State app) {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
			keys.DumpBindings(nameof(BrowserScreen));
		}
		return Task.CompletedTask;
	}

	private void ConfigureKeys() {
		static bool IsFilterChar(char ch) => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/';

		ConfigureDefaultGlobalKeys();
		keys.RegisterKey(KeyCode.Down, "↓", "move", KEY_Down);
		keys.RegisterKey(KeyCode.Up, "↑", "move", KEY_Up);
		keys.RegisterKey(KeyCode.PAGE_DOWN, "PgDn", "jump", KEY_JumpDown);
		keys.RegisterKey(KeyCode.PAGE_UP, "PgUp", "jump", KEY_JumpUp);
		keys.RegisterKey(KeyCode.Left, "←", "switch focus", KEY_Tab);
		keys.RegisterChar('j', "↓", KEY_Down);
		keys.RegisterChar('k', "↑", KEY_Up);
		keys.RegisterKey(KeyCode.TAB, "Tab", "switch focus", KEY_Tab);
		keys.RegisterKey(KeyCode.ENTER, "Enter", "open/summarize", KEY_Enter);
		keys.RegisterChar('o', "open in editor", KEY_OpenInEditor);
		keys.RegisterChar('/', "filter", KEY_Filter);
		keys.Register(
			"char",
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
			});
		keys.RegisterKey(KeyCode.Delete, "Backspace", "erase", KEY_Erase);
		return;
	}

	private bool KEY_Tab(ThaumTUI.State a) {
		a.focus = a.focus == ThaumTUI.Panel.Files ? ThaumTUI.Panel.Symbols : ThaumTUI.Panel.Files;
		return true;
	}

	private bool KEY_Erase(ThaumTUI.State a) {
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
			CodeSymbol s = a.visibleSymbols.Selected;
			opener.Open(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
			return true;
		}
		return false;
	}

	private bool KEY_Enter(ThaumTUI.State a) {
		switch (a.focus) {
			case ThaumTUI.Panel.Files when a.visibleFiles.Count > 0: {
				string file = a.visibleFiles.Selected;
				a.visibleSymbols.Reset(a.allSymbols.Where(s => s.FilePath == file), 0);
				a.symOffset = 0;
				a.summary   = null;
				return true;
			}
			case ThaumTUI.Panel.Symbols when a.visibleSymbols.Count > 0 && !a.isLoading:
				StartTask(async _ => {
					a.isLoading = true;
					try {
						a.summary = await tui.LoadSymbolDetail(a.visibleSymbols.Selected);
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
				a.SelectedFile = Math.Max(a.SelectedFile - 10, 0);
				tui.EnsureVisible(ref a.fileOffset, a.SelectedFile);
				return true;
			case ThaumTUI.Panel.Symbols when a.visibleSymbols.Count > 0:
				a.SelectedSymbol = Math.Max(a.SelectedSymbol - 10, 0);
				tui.EnsureVisible(ref a.symOffset, a.SelectedSymbol);
				a.summary = null;
				return true;
			default:
				return false;
		}
	}

	private bool KEY_JumpDown(ThaumTUI.State a) {
		switch (a.focus) {
			case ThaumTUI.Panel.Files when a.visibleFiles.Count > 0:
				a.SelectedFile = Math.Min(a.SelectedFile + 10, a.visibleFiles.Count - 1);
				tui.EnsureVisible(ref a.fileOffset, a.SelectedFile);
				return true;
			case ThaumTUI.Panel.Symbols when a.visibleSymbols.Count > 0:
				a.SelectedSymbol = Math.Min(a.SelectedSymbol + 10, a.visibleSymbols.Count - 1);
				tui.EnsureVisible(ref a.symOffset, a.SelectedSymbol);
				a.summary = null;
				return true;
			default:
				return false;
		}
	}

	private bool KEY_Up(ThaumTUI.State a) {
		if (a.visibleFiles.Count > 0 && a.focus == ThaumTUI.Panel.Files) {
			a.SelectedFile = Math.Max(a.SelectedFile - 1, 0);
			tui.EnsureVisible(ref a.fileOffset, a.SelectedFile);
			return true;
		}
		if (a.visibleSymbols.Count > 0 && a.focus == ThaumTUI.Panel.Symbols) {
			a.SelectedSymbol = Math.Max(a.SelectedSymbol - 1, 0);
			tui.EnsureVisible(ref a.symOffset, a.SelectedSymbol);
			a.summary = null;
			return true;
		}
		return false;
	}

	private bool KEY_Down(ThaumTUI.State a) {
		if (a.visibleFiles.Count > 0 && a.focus == ThaumTUI.Panel.Files) {
			a.SelectedFile = Math.Min(a.SelectedFile + 1, a.visibleFiles.Count - 1);
			tui.EnsureVisible(ref a.fileOffset, a.SelectedFile);
			return true;
		}
		if (a.visibleSymbols.Count > 0 && a.focus == ThaumTUI.Panel.Symbols) {
			a.SelectedSymbol = Math.Min(a.SelectedSymbol + 1, a.visibleSymbols.Count - 1);
			tui.EnsureVisible(ref a.symOffset, a.SelectedSymbol);
			a.summary = null;
			return true;
		}
		return false;
	}

	private string FileLine(string path) {
		try {
			return Path.GetRelativePath(projectPath, path);
		} catch {
			return Path.GetFileName(path);
		}
	}

	private static string SymbolLine(CodeSymbol s)
		=> $"{s.Kind switch { SymbolKind.Class => "[C]", SymbolKind.Method => "[M]", SymbolKind.Function => "[F]", SymbolKind.Interface => "[I]", SymbolKind.Enum => "[E]", _ => "[·]" }} {s.Name.Replace('\n', ' ')}";

	private static void ApplyFileFilter(ThaumTUI.State app) {
		app.visibleFiles.Reset(string.IsNullOrWhiteSpace(app.fileFilter)
			? app.allFiles
			: app.allFiles.Where(p => p.ToLowerInvariant().Contains(app.fileFilter.ToLowerInvariant())), 0);

		app.fileOffset = 0;
		app.summary    = null;
		string? file = app.visibleFiles.SafeSelected;
		if (file is null) app.visibleSymbols.Clear();
		else app.visibleSymbols.Reset(app.allSymbols.Where(s => s.FilePath == file), 0);
		app.symOffset = 0;
	}

	private static void ApplySymbolFilter(ThaumTUI.State app) {
		string?                 file    = app.visibleFiles.SafeSelected;
		IEnumerable<CodeSymbol> baseSet = string.IsNullOrEmpty(file) ? app.allSymbols : app.allSymbols.Where(s => s.FilePath == file);
		app.visibleSymbols.Reset(string.IsNullOrWhiteSpace(app.symFilter)
			? baseSet
			: baseSet.Where(s => s.Name.ToLowerInvariant().Contains(app.symFilter.ToLowerInvariant())), 0);
		app.symOffset = 0;
		app.summary   = null;
	}

	// Paragraph-based fallback retained for reference; not used after frame-mode support.
}