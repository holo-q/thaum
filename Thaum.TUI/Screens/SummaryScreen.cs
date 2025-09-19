using Ratatui;
using Thaum.Core.Crawling;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.RatLayout;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

public sealed class SummaryScreen : Screen {
	private bool _keysReady;

	public SummaryScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
		: base(tui, opener, projectPath) { }

	public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath) {
        Paragraph title = Paragraph("", title: "Summary", title_border: true);
        (Rect titleRect, Rect bodyRect) = area.SplitTop(2);
        term.Draw(title, titleRect);
        string          bodyText = app.isLoading ? $"Summarizing… {Styles.Spinner()}" : (app.summary ?? "No summary yet. Press 3 to (re)generate.");
        Paragraph para     = Paragraph("");
        if (bodyText.StartsWith("Error:")) para.AppendSpan(bodyText, Styles.S_ERROR);
        else para.AppendSpan(bodyText);
        term.Draw(para, bodyRect);
	}

    public override Task OnEnter(ThaumTUI.State app) {
        if (!_keysReady) { ConfigureKeys(); _keysReady = true; keys.DumpBindings(nameof(SummaryScreen)); }
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
						a.summary = await tui.LoadSymbolDetail(a.visibleSymbols.Selected);
				} finally { a.isLoading = false; }
			});
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

	// Key help comes from KeybindManager registrations
}
