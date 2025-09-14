using Ratatui;
using Thaum.Core.Models;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.RatLayout;

namespace Thaum.App.RatatuiTUI;

internal sealed class SummaryScreen : Screen {
	private bool _keysReady;

	public SummaryScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
		using Paragraph title = Paragraph("", title: "Summary", title_border: true);
		term.Draw(title, R(area.X, area.Y, area.Width, 2));
		string          bodyText = app.isLoading ? $"Summarizing… {TuiTheme.Spinner()}" : (app.summary ?? "No summary yet. Press 3 to (re)generate.");
		using Paragraph para     = Paragraph("");
		if (bodyText.StartsWith("Error:")) para.AppendSpan(bodyText, TuiTheme.Error);
		else para.AppendSpan(bodyText);
		term.Draw(para, R(area.X, area.Y + 2, area.Width, area.Height - 2));
	}

	public override Task OnEnter(ThaumTUI.State app) {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
		}
		return Task.CompletedTask;
	}

	public override string FooterHint(ThaumTUI.State app)
		=> (app.isLoading ? "Summarizing…" : "");

	public override string Title(ThaumTUI.State app) => "Summary";

	private void ConfigureKeys() {
		ConfigureDefaultGlobalKeys();
		keys
			.RegisterChar('3', "summarize", KEY_Summarize)
			.RegisterChar('o', "open in editor", KEY_OpenInEditor);
	}

	private bool KEY_Summarize(ThaumTUI.State a) {
		if (a is { isLoading: false, visibleSymbols.Count: > 0 } && string.IsNullOrEmpty(a.summary)) {
			StartTask(async _ => {
				a.isLoading = true;
				try {
					a.summary = await tui.LoadSymbolDetail(a.visibleSymbols[a.symSelected]);
				} finally { a.isLoading = false; }
			});
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

	// Key help comes from KeybindManager registrations
}