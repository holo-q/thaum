using System.Text;
using Ratatui;
using Ratatui.Sugar;
using static Thaum.App.RatatuiTUI.Rat;

namespace Thaum.App.RatatuiTUI;

public sealed class ReferencesScreen : ThaumScreen {
	private bool _keysReady;

	public ReferencesScreen(ThaumTUI tui)
		: base(tui) { }

	public override void Draw(Terminal tm, Rect area) {
		Paragraph title = Title("References", true);
		(Rect titleRect, Rect listRect) = area.SplitTop(2);
		tm.Draw(title, titleRect);
		List list = List();
		int  view = Math.Max(1, listRect.h);
		if (model.refs is { Count: > 0 }) {
			if (model.refsSelected < model.refsOffset) model.refsOffset         = model.refsSelected;
			if (model.refsSelected >= model.refsOffset + view) model.refsOffset = Math.Max(0, model.refsSelected - (view - 1));
		}
		List<CodeRef> refs  = model.refs ?? new List<CodeRef>();
		int           start = Math.Max(0, model.refsOffset);
		int           end   = Math.Min(refs.Count, start + view);
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
		tm.Draw(list, listRect);
	}

	public override Task OnEnter() {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
			keys.DumpBindings(nameof(ReferencesScreen));
		}

		// Load refs asynchronously so any crawler exceptions are surfaced as screen ErrorMessage
		tui.tasks.Start("Load Refs", async _ => {
			await model.EnsureRefs();
		});

		return Task.CompletedTask;
	}

	public override string FooterMsg => "↑/↓ scroll  o open";

	public override string TitleMsg => "References";

	private void ConfigureKeys() {
		keys
			.RegisterKey(KeyCode.DOWN, "↓", "move", KEY_Down)
			.RegisterKey(KeyCode.UP, "↑", "move", KEY_Up)
			.RegisterKey(KeyCode.PAGE_DOWN, "PgDn", "move", KEY_PageDown)
			.RegisterKey(KeyCode.PAGE_UP, "PgUp", "move", Key_PageUp)
			.RegisterChar('o', "open in editor", KEY_OpenInEditor);
	}

	private bool KEY_OpenInEditor(ThaumTUI tui) {
		if (model.refs is { Count: > 0 }) {
			(string f, int ln, string _) = model.refs[Math.Min(model.refsSelected, model.refs.Count - 1)];
			SysUtil.OpenInEditor(tui.projectPath, f, Math.Max(1, ln));
			return true;
		}
		return false;
	}

	private bool Key_PageUp(ThaumTUI tui) {
		if (model.refs is { Count: > 0 }) {
			model.refsSelected = Math.Max(model.refsSelected - 10, 0);
			tui.EnsureVisible(ref model.refsOffset, model.refsSelected);
			return true;
		}
		return false;
	}

	private bool KEY_PageDown(ThaumTUI tui) {
		if (model.refs is { Count: > 0 }) {
			model.refsSelected = Math.Min(model.refsSelected + 10, model.refs.Count - 1);
			tui.EnsureVisible(ref model.refsOffset, model.refsSelected);
			return true;
		}
		return false;
	}

	private bool KEY_Up(ThaumTUI tui) {
		if (model.refs is { Count: > 0 }) {
			model.refsSelected = Math.Max(model.refsSelected - 1, 0);
			tui.EnsureVisible(ref model.refsOffset, model.refsSelected);
			return true;
		}
		return false;
	}

	private bool KEY_Down(ThaumTUI tui) {
		if (model.refs is { Count: > 0 }) {
			model.refsSelected = Math.Min(model.refsSelected + 1, model.refs.Count - 1);
			tui.EnsureVisible(ref model.refsOffset, model.refsSelected);
			return true;
		}
		return false;
	}

	// Key help comes from KeybindManager registrations
}