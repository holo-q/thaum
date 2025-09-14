using System.Text;
using Ratatui;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.RatLayout;

namespace Thaum.App.RatatuiTUI;

internal sealed class ReferencesScreen : Screen {
	private bool _keysReady;

	public ReferencesScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
		using Paragraph title = Paragraph("", title: "References", title_border: true);
		term.Draw(title, R(area.X, area.Y, area.Width, 2));
		using List          list  = List();
		List<CodeRef>       refs  = app.refs ?? new List<CodeRef>();
		int                 start = Math.Max(0, app.refsOffset);
		int                 end   = Math.Min(refs.Count, start + Math.Max(1, area.Height - 2));
		for (int i = start; i < end; i++) {
			(string f, int ln, string nm) = refs[i];
			Memory<byte> lhs = Encoding.UTF8.GetBytes($"{Path.GetFileName(f)}:{ln}  ").AsMemory();
			Memory<byte> rhs = Encoding.UTF8.GetBytes(nm).AsMemory();
			ReadOnlyMemory<Batching.SpanRun> runs = new[] {
				new Batching.SpanRun(lhs, TuiTheme.FilePath),
				new Batching.SpanRun(rhs, default)
			};
			list.AppendItem(runs.Span);
		}
		term.Draw(list, R(area.X, area.Y + 2, area.Width, area.Height - 2));
	}

	public override Task OnEnter(ThaumTUI.State app) {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
		}
		return tui.EnsureRefs(app);
	}

	public override string FooterHint(ThaumTUI.State app) => "↑/↓ scroll  o open";

	public override string Title(ThaumTUI.State app) => "References";

	private void ConfigureKeys() {
		ConfigureDefaultGlobalKeys();
		keys
			.RegisterKey(KeyCode.Down, "↓", "move", KEY_Down)
			.RegisterKey(KeyCode.Up, "↑", "move", KEY_Up)
			.RegisterKey(KeyCode.PageDown, "PgDn", "move", KEY_PageDown)
			.RegisterKey(KeyCode.PageUp, "PgUp", "move", Key_PageUp)
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
