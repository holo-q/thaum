using System.Diagnostics.CodeAnalysis;
using Ratatui;
using Ratatui.Layout;
using Ratatui.Sugar;
using Thaum.Core.Crawling;
using static Thaum.App.RatatuiTUI.RatLayout;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.Styles;

namespace Thaum.App.RatatuiTUI;

/// <summary>
/// Presents the browser view with file and symbol lists alongside symbol metadata.
/// </summary>
public sealed class BrowserScreen : ThaumScreen {
	private bool _keysReady;

	public BrowserScreen(ThaumTUI tui) : base(tui) { }

	private void ConfigureKeys() {
		static bool IsFilterChar(char ch) => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' || ch == '.' || ch == '/';

		// ConfigureDefaultGlobalKeys();
		tui.keys.RegisterKey(KeyCode.DOWN, "↓", "move", KEY_Down);
		tui.keys.RegisterKey(KeyCode.UP, "↑", "move", KEY_Up);
		tui.keys.RegisterKey(KeyCode.PAGE_DOWN, "PgDn", "jump", KEY_JumpDown);
		tui.keys.RegisterKey(KeyCode.PAGE_UP, "PgUp", "jump", KEY_JumpUp);
		tui.keys.RegisterKey(KeyCode.LEFT, "←", "switch focus", KEY_Tab);
		tui.keys.RegisterChar('j', "↓", KEY_Down);
		tui.keys.RegisterChar('k', "↑", KEY_Up);
		tui.keys.RegisterKey(KeyCode.TAB, "Tab", "switch focus", KEY_Tab);
		tui.keys.RegisterKey(KeyCode.ENTER, "Enter", "open/summarize", KEY_Enter);
		tui.keys.RegisterChar('o', "open in editor", KEY_OpenInEditor);
		tui.keys.RegisterChar('/', "filter", KEY_Filter);
		tui.keys.Register(
			"char",
			"type filter",
			ev => ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.CHAR } && IsFilterChar((char)ev.Key.CharUint),
			(ev, tui) => {
				if (model.focus == ThaumTUI.Panel.Files) {
					model.fileFilter += (char)ev.Key.CharUint;
					model.ApplyFileFilter();
				} else {
					model.symFilter += (char)ev.Key.CharUint;
					model.ApplySymbolFilter();
				}
				return true;
			});
		tui.keys.RegisterKey(KeyCode.DELETE, "Backspace", "erase", KEY_Erase);
		return;
	}

	// public override void Draw(Terminal term, Rect area, ThaumTUI tui, string projectPath) {
	// 	throw new NotImplementedException();
	// }

	public override string TitleMsg
		=> $"Browser — Focus: {(tui.model.focus == ThaumTUI.Panel.Files ? "Files" : "Symbols")}";

	public override string FooterMsg
		=> "↑/↓ move  Tab switch  / filter  Enter open/summarize  o open";

	[SuppressMessage("ReSharper", "InconsistentNaming")]
	public override void Draw(Terminal tm, Rect area) {
		var state = tui.model;

		bool hasSym     = state.visibleSymbols.Count > 0;
		bool filesFocus = state.focus == ThaumTUI.Panel.Files;
		bool symsFocus  = state.focus == ThaumTUI.Panel.Symbols;

		IReadOnlyList<Rect> cols = SplitV(area, [
			Constraint.Percentage(30),
			Constraint.Percentage(30),
			Constraint.Percentage(40)
		], gap: 1, margin: 0);

		string t1 = state.fileFilter.Length == 0 ? "(/ to filter)" : $"/{state.fileFilter}";
		string t2 = state.symFilter.Length == 0 ? "(type to filter)" : $"/{state.symFilter}";

		Rect      r1        = cols[0];
		Paragraph r1title   = Title("Files", true).Line(t1);
		Rect      r1inner   = r1.Inner();
		Rect      r1content = r1inner.Body();

		Rect      r2      = cols[1];
		Paragraph r2title = Title("Symbols", true).Line(t2);
		Rect      r2inner = r2.Inner();
		(Rect r2header, Rect r2content) = r2inner.SplitTop(1);

		Rect r3 = cols[2];
		(Rect rDetails, Rect rSummary) = r3.SplitTop(Math.Max(6, r3.h / 3));

		int filesView = Math.Max(1, r1content.h);
		int symsView  = Math.Max(1, r2content.h);

		// FILES
		// full-height border with title; draw content inside inner rect
		// ----------------------------------------
		tm.Draw(r1title, r1);

		Paragraph lbFiles = Title(filesFocus ? " Files " : " files ");
		// lbFiles += (filesFocus ? S_TITLE_ACTIVE : S_TITLE_DIM) |
		//            (filesFocus ? " Files " : " files ");
		// term.Draw(lbFiles, r1inner.WithMinSize(1, 1));

		string FileLine(string path) {
			try {
				return Path.GetRelativePath(tui.projectPath, path);
			} catch {
				return Path.GetFileName(path);
			}
		}

		state.visibleFiles.Draw(tm, r1content, 0, FileLine, _ => S_PATH);

		// SYMBOLS
		// full-height border with title; draw content inside inner rect
		// ----------------------------------------
		tm.Draw(r2title, r2);
		tm.Draw(
			Title(symsFocus ? " Symbols " : " symbols ").Line(
				symsFocus ? " Symbols " : " symbols ",
				symsFocus ? S_TITLE_ACTIVE : S_TITLE_DIM
			),
			r2header.WithMinSize(minWidth: 1, minHeight: 1));

		state.visibleSymbols.Draw(tm, r2content, 0, SymbolLine, s => ThaumStyles.StyleForKind(s.Kind));

		tm.Draw(Title("Details", true)
				.Line(!hasSym ? "" : $"Name: {state.visibleSymbols.Selected.Name}")
				.Line(!hasSym ? "" : $"Kind: {state.visibleSymbols.Selected.Kind}")
				.Line(!hasSym ? "" : $"File: {state.visibleSymbols.Selected.FilePath}")
				.Line(!hasSym ? "" : $"Span: L{state.visibleSymbols.Selected.StartCodeLoc.Line}:C{state.visibleSymbols.Selected.StartCodeLoc.Character}"),
			rDetails);

		bool isLoadingCurrent = state.visibleSymbols.Count > 0 && state.IsSymbolLoading(state.visibleSymbols.Selected);
		string body = isLoadingCurrent
			? $"Summarizing… {Spinner()}"
			: state.summary ?? "";

		Paragraph detail = Title("Summary", true);
		if (body.StartsWith("Error:"))
			detail += body | S_ERROR;
		else
			detail += body;

		tm.Draw(detail, rSummary.WithMinSize(minHeight: 1));
	}


	// Key help comes from KeybindManager registrations
	public override Task OnEnter() {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
			keys.DumpBindings(nameof(BrowserScreen));
		}
		return Task.CompletedTask;
	}


	private bool KEY_Tab(ThaumTUI tui) {
		var a = tui.model;
		a.focus = a.focus == ThaumTUI.Panel.Files ? ThaumTUI.Panel.Symbols : ThaumTUI.Panel.Files;
		return true;
	}

	private bool KEY_Erase(ThaumTUI tui) {
		switch (model.focus) {
			case ThaumTUI.Panel.Files when model.fileFilter.Length > 0:
				model.fileFilter = model.fileFilter[..^1];
				model.ApplyFileFilter();
				return true;
			case ThaumTUI.Panel.Symbols when model.symFilter.Length > 0:
				model.symFilter = model.symFilter[..^1];
				model.ApplySymbolFilter();
				return true;
			default:
				return false;
		}
	}

	private bool KEY_Filter(ThaumTUI tui) {
		if (model.focus == ThaumTUI.Panel.Files) {
			model.fileFilter = string.Empty;
			model.ApplyFileFilter();
		} else {
			model.symFilter = string.Empty;
			model.ApplySymbolFilter();
		}
		return true;
	}

	private bool KEY_OpenInEditor(ThaumTUI tui) {
		var a = tui.model;
		if (a.visibleSymbols.Count > 0) {
			CodeSymbol s = a.visibleSymbols.Selected;
			SysUtil.OpenInEditor(tui.projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
			return true;
		}
		return false;
	}

	private bool KEY_Enter(ThaumTUI tui) {
		switch (model.focus) {
			case ThaumTUI.Panel.Files when model.visibleFiles.Count > 0: {
				string file = model.visibleFiles.Selected;
				model.visibleSymbols.Reset(model.allSymbols.Where(s => s.FilePath == file), 0);
				model.symOffset = 0;
				model.summary   = null;
				return true;
			}
			case ThaumTUI.Panel.Symbols when model.visibleSymbols.Count > 0: {
				CodeSymbol currentSymbol = model.visibleSymbols.Selected;

				// Don't start if already loading this symbol
				if (model.IsSymbolLoading(currentSymbol)) {
					return true;
				}

				var task = tui.tasks.Start("Summarize", async _ => {
					try {
						model.summary = await tui.LoadSymbolDetail(currentSymbol);
					} finally {
						model.CompleteSymbolTask(currentSymbol);
					}
				}, currentSymbol);

				model.StartSymbolTask(currentSymbol, task);
				return true;
			}
			default:
				return false;
		}
	}


	private bool KEY_JumpUp(ThaumTUI tui) {
		var a = tui.model;
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

	private bool KEY_JumpDown(ThaumTUI tui) {
		var a = tui.model;
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

	private bool KEY_Up(ThaumTUI tui) {
		var a = tui.model;
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

	private bool KEY_Down(ThaumTUI tui) {
		var a = tui.model;
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


	private static string SymbolLine(CodeSymbol s)
		=> $"{s.Kind switch { SymbolKind.Class => "[C]", SymbolKind.Method => "[M]", SymbolKind.Function => "[F]", SymbolKind.Interface => "[I]", SymbolKind.Enum => "[E]", _ => "[·]" }} {s.Name.Replace('\n', ' ')}";
}