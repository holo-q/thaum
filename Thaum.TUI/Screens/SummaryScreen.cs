using Ratatui;
using Ratatui.Sugar;
using Thaum.Core.Crawling;
using static Thaum.App.RatatuiTUI.Rat;

namespace Thaum.App.RatatuiTUI;

public sealed class SummaryScreen : ThaumScreen {
	private bool _keysReady;

	public SummaryScreen(ThaumTUI tui)
		: base(tui) { }

	public override void Draw(Terminal tm, Rect area) {
		Paragraph title = Title("Summary", true);
		(Rect titleRect, Rect bodyRect) = area.SplitTop(2);

		tm.Draw(title, titleRect);

		bool isCurrentSymbolLoading = model.visibleSymbols.Count > 0 && model.IsSymbolLoading(model.visibleSymbols.Selected);
		string bodyText = isCurrentSymbolLoading
			? $"Summarizing… {Styles.Spinner()}"
			: (model.summary ?? "No summary yet. Press 3 to (re)generate.");

		Paragraph p = Paragraph();
		if (bodyText.StartsWith("Error:"))
			p.Span(bodyText, Styles.S_ERROR);
		else
			p.Span(bodyText);
		tm.Draw(p, bodyRect);
	}

	public override Task OnEnter() {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
			keys.DumpBindings(nameof(SummaryScreen));
		}
		return Task.CompletedTask;
	}

	public override string FooterMsg => (model.visibleSymbols.Count > 0 && model.IsSymbolLoading(model.visibleSymbols.Selected)) ? "Summarizing…" : "";

	public override string TitleMsg => "Summary";

	private void ConfigureKeys() {
		keys
			.RegisterChar('3', "summarize", KEY_Summarize)
			.RegisterChar('o', "open in editor", KEY_OpenInEditor);
	}

	private bool KEY_Summarize(ThaumTUI tui) {
		if (model.visibleSymbols.Count > 0) {
			CodeSymbol currentSymbol = model.visibleSymbols.Selected;

			// Don't start if already loading this symbol
			if (model.IsSymbolLoading(currentSymbol)) {
				return true;
			}

			// Only start if no summary exists or we want to regenerate
			if (string.IsNullOrEmpty(model.summary)) {
				var task = tui.tasks.Start("Summarize", async _ => {
					try {
						model.summary = await tui.LoadSymbolDetail(currentSymbol);
					} finally {
						model.CompleteSymbolTask(currentSymbol);
					}
				}, currentSymbol);

				model.StartSymbolTask(currentSymbol, task);
			}
		}
		return true;
	}

	private bool KEY_OpenInEditor(ThaumTUI tui) {
		if (model.visibleSymbols.Count > 0) {
			CodeSymbol s = model.visibleSymbols.Selected;
			SysUtil.OpenInEditor(tui.projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
			return true;
		}
		return false;
	}

	// Key help comes from KeybindManager registrations
}