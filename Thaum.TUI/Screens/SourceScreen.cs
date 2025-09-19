using System.Runtime.InteropServices;
using System.Text;
using Ratatui;
using Thaum.Core.Crawling;
using Thaum.Core.Models;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.RatLayout;

namespace Thaum.App.RatatuiTUI;

/// <summary>
/// Shows the source code for the currently selected symbol with syntax highlighting.
/// </summary>
public class SourceScreen : Screen {
	public SourceScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
        Paragraph title = Paragraph("", title: "Source", title_border: true);
        (Rect titleRect, Rect listRect) = area.SplitTop(2);
        term.Draw(title, titleRect);

        List<string> lines = app.sourceLines ?? new List<string>();
        List   list  = List();
        int view = Math.Max(1, listRect.Height - 1);
        if (app.sourceSelected < app.sourceOffset) app.sourceOffset = app.sourceSelected;
        if (app.sourceSelected >= app.sourceOffset + view) app.sourceOffset = Math.Max(0, app.sourceSelected - (view - 1));
        int start = Math.Max(0, app.sourceOffset);
        int end   = Math.Min(lines.Count, start + view);

		int symStartLine = 0, symEndLine = -1, symStartCol = 0, symEndCol = 0;
		if (app.visibleSymbols.Count > 0) {
			CodeSymbol s = app.visibleSymbols.Selected;
			symStartLine = Math.Max(1, s.StartCodeLoc.Line);
			symEndLine   = Math.Max(symStartLine, s.EndCodeLoc.Line);
			symStartCol  = Math.Max(0, s.StartCodeLoc.Character);
			symEndCol    = Math.Max(symStartCol, s.EndCodeLoc.Character);
		}

		for (int i = start; i < end; i++) {
			string                 num      = $"{(i + 1).ToString().PadLeft(5)}  ";
			Memory<byte>           ln       = Encoding.UTF8.GetBytes(num).AsMemory();
			string                 line     = lines[i];
			List<Batching.SpanRun> runs     = new List<Batching.SpanRun>(4) { new Batching.SpanRun(ln, Styles.S_LINENUM) };
			int                    oneBased = i + 1;
			if (oneBased >= symStartLine && oneBased <= symEndLine) {
				int sc = (oneBased == symStartLine) ? symStartCol : 0;
				int ec = (oneBased == symEndLine) ? symEndCol : line.Length;
				sc = Math.Clamp(sc, 0, line.Length);
				ec = Math.Clamp(ec, sc, line.Length);
				string pre = line[..sc], mid = line[sc..ec], post = line[ec..];
				if (pre.Length > 0) runs.Add(new Batching.SpanRun(Encoding.UTF8.GetBytes(pre).AsMemory(), default));
				if (mid.Length > 0) runs.Add(new Batching.SpanRun(Encoding.UTF8.GetBytes(mid).AsMemory(), Styles.S_CODEHI));
				if (post.Length > 0) runs.Add(new Batching.SpanRun(Encoding.UTF8.GetBytes(post).AsMemory(), default));
			} else {
				runs.Add(new Batching.SpanRun(Encoding.UTF8.GetBytes(line).AsMemory(), default));
			}
            list.AppendItem(CollectionsMarshal.AsSpan(runs));
        }
        term.Draw(list, listRect);
	}

    public override Task OnEnter(ThaumTUI.State app) {
        if (!_keysReady) { ConfigureKeys(); _keysReady = true; keys.DumpBindings(nameof(SourceScreen)); }
        return tui.EnsureSource(app);
    }

	private bool _keysReady;

	private void ConfigureKeys() {
		ConfigureDefaultGlobalKeys();
		keys
			.RegisterKey(KeyCode.Down, "↓", "scroll", KEY_Down)
			.RegisterKey(KeyCode.Up, "↑", "scroll", KEY_Up)
			.RegisterKey(KeyCode.PAGE_DOWN, "PgDn", "scroll", KEY_PageDown)
			.RegisterKey(KeyCode.PAGE_UP, "PgUp", "scroll", KEY_PageUp)
			.RegisterChar('o', "open in editor", KEY_OpenInEditor);
	}

	private bool KEY_Down(ThaumTUI.State a) {
		if (a.sourceLines is { Count: > 0 }) {
			a.sourceSelected = Math.Min(a.sourceSelected + 1, a.sourceLines!.Count - 1);
			tui.EnsureVisible(ref a.sourceOffset, a.sourceSelected);
			return true;
		}
		return false;
	}

	private bool KEY_Up(ThaumTUI.State a) {
		if (a.sourceLines is { Count: > 0 }) {
			a.sourceSelected = Math.Max(a.sourceSelected - 1, 0);
			tui.EnsureVisible(ref a.sourceOffset, a.sourceSelected);
			return true;
		}
		return false;
	}

	private bool KEY_PageDown(ThaumTUI.State a) {
		if (a.sourceLines is { Count: > 0 }) {
			a.sourceSelected = Math.Min(a.sourceSelected + 10, a.sourceLines!.Count - 1);
			tui.EnsureVisible(ref a.sourceOffset, a.sourceSelected);
			return true;
		}
		return false;
	}

	private bool KEY_PageUp(ThaumTUI.State a) {
		if (a.sourceLines is { Count: > 0 }) {
			a.sourceSelected = Math.Max(a.sourceSelected - 10, 0);
			tui.EnsureVisible(ref a.sourceOffset, a.sourceSelected);
			return true;
		}
		return false;
	}

	private bool KEY_OpenInEditor(ThaumTUI.State a) {
		if (a.visibleSymbols.Count > 0) {
			CodeSymbol s = a.visibleSymbols.Selected;
			opener.Open(projectPath, s.FilePath, Math.Max(1, a.sourceSelected + 1));
			return true;
		}
		return false;
	}

	public override string FooterHint(ThaumTUI.State app) => "↑/↓ scroll  o open";

	public override string Title(ThaumTUI.State app) => "Source";

	// Key help comes from KeybindManager registrations
}
