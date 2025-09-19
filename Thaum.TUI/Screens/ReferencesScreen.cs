using System.Text;
using Ratatui;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.RatLayout;

namespace Thaum.App.RatatuiTUI;

public sealed class ReferencesScreen : Screen {
	private bool _keysReady;

	public ReferencesScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
        Paragraph title = Paragraph("", title: "References", title_border: true);
        (Rect titleRect, Rect listRect) = area.SplitTop(2);
        term.Draw(title, titleRect);
        List          list  = List();
        int view = Math.Max(1, listRect.Height);
        if (app.refs is { Count: > 0 }) {
            if (app.refsSelected < app.refsOffset) app.refsOffset = app.refsSelected;
            if (app.refsSelected >= app.refsOffset + view) app.refsOffset = Math.Max(0, app.refsSelected - (view - 1));
        }
        List<CodeRef>       refs  = app.refs ?? new List<CodeRef>();
        int                 start = Math.Max(0, app.refsOffset);
        int                 end   = Math.Min(refs.Count, start + view);
		for (int i = start; i < end; i++) {
			(string f, int ln, string nm) = refs[i];
			Memory<byte> lhs = Encoding.UTF8.GetBytes($"{Path.GetFileName(f)}:{ln}  ").AsMemory();
			Memory<byte> rhs = Encoding.UTF8.GetBytes(nm).AsMemory();
			ReadOnlyMemory<Batching.SpanRun> runs = new[] {
				new Batching.SpanRun(lhs, Styles.S_PATH),
				new Batching.SpanRun(rhs, default)
			};
			list.AppendItem(runs.Span);
		}
        term.Draw(list, listRect);
	}

    public override Task OnEnter(ThaumTUI.State app) {
        if (!_keysReady) { ConfigureKeys(); _keysReady = true; keys.DumpBindings(nameof(ReferencesScreen)); }
        // Load refs asynchronously so any crawler exceptions are surfaced as screen ErrorMessage
        StartTask(async _ => { await tui.EnsureRefs(app); });
        return Task.CompletedTask;
    }

	public override string FooterHint(ThaumTUI.State app) => "↑/↓ scroll  o open";

	public override string Title(ThaumTUI.State app) => "References";

	private void ConfigureKeys() {
		ConfigureDefaultGlobalKeys();
		keys
			.RegisterKey(KeyCode.Down, "↓", "move", KEY_Down)
			.RegisterKey(KeyCode.Up, "↑", "move", KEY_Up)
			.RegisterKey(KeyCode.PAGE_DOWN, "PgDn", "move", KEY_PageDown)
			.RegisterKey(KeyCode.PAGE_UP, "PgUp", "move", Key_PageUp)
			.RegisterChar('o', "open in editor", KEY_OpenInEditor);
	}

	private bool KEY_OpenInEditor(ThaumTUI.State a) {
		if (a.refs is { Count: > 0 }) {
			(string f, int ln, string _) = a.refs[Math.Min(a.refsSelected, a.refs.Count - 1)];
			opener.Open(projectPath, f, Math.Max(1, ln));
			return true;
		}
		return false;
	}

	private bool Key_PageUp(ThaumTUI.State a) {
		if (a.refs is { Count: > 0 }) {
			a.refsSelected = Math.Max(a.refsSelected - 10, 0);
			tui.EnsureVisible(ref a.refsOffset, a.refsSelected);
			return true;
		}
		return false;
	}

	private bool KEY_PageDown(ThaumTUI.State a) {
		if (a.refs is { Count: > 0 }) {
			a.refsSelected = Math.Min(a.refsSelected + 10, a.refs.Count - 1);
			tui.EnsureVisible(ref a.refsOffset, a.refsSelected);
			return true;
		}
		return false;
	}

	private bool KEY_Up(ThaumTUI.State a) {
		if (a.refs is { Count: > 0 }) {
			a.refsSelected = Math.Max(a.refsSelected - 1, 0);
			tui.EnsureVisible(ref a.refsOffset, a.refsSelected);
			return true;
		}
		return false;
	}

	private bool KEY_Down(ThaumTUI.State a) {
		if (a.refs is { Count: > 0 }) {
			a.refsSelected = Math.Min(a.refsSelected + 1, a.refs.Count - 1);
			tui.EnsureVisible(ref a.refsOffset, a.refsSelected);
			return true;
		}
		return false;
	}

	// Key help comes from KeybindManager registrations
}
