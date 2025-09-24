using Ratatui;
using Ratatui.Sugar;
using Thaum.Core.Crawling;
using static Ratatui.Sugar.Rat;
using static Ratatui.Sugar.Styles;

namespace Thaum.App.RatatuiTUI;

public sealed class InfoScreen : ThaumScreen {
	private bool _keysReady;

	public InfoScreen(ThaumTUI tui) : base(tui) { }

	public override void Draw(Terminal tm, Rect area) {
		Paragraph title = Title("Info", true);
		(Rect titleRect, Rect detailRect) = area.SplitTop(2);
		tm.Draw(title, titleRect);
		if (model.visibleSymbols.Count == 0) return;
		CodeSymbol s = model.visibleSymbols.Selected;

		Paragraph para = Paragraph();
		para.Span("Name: ", S_HINT).Span(s.Name, ThaumStyles.StyleForKind(s.Kind)).Line();
		para.Span("Kind: ", S_HINT).Span(s.Kind.ToString(), S_INFO).Line();
		para.Span("File: ", S_HINT).Span(s.FilePath, S_PATH).Line();
		para.Span("Start: ", S_HINT).Span($"L{s.StartCodeLoc.Line}", S_LINENUM).Span(":", S_HINT).Span($"C{s.StartCodeLoc.Character}", S_LINENUM).Line();
		para.Span("End:   ", S_HINT).Span($"L{s.EndCodeLoc.Line}", S_LINENUM).Span(":", S_HINT).Span($"C{s.EndCodeLoc.Character}", S_LINENUM).Line();
		para.Span("Children: ", S_HINT).Span((s.Children?.Count ?? 0).ToString(), S_INFO).Line();
		para.Span("Deps: ", S_HINT).Span((s.Dependencies?.Count ?? 0).ToString(), S_INFO).Line();
		para.Span("Last: ", S_HINT).Span((s.LastModified?.ToString("u") ?? "n/a"), S_INFO);
		tm.Draw(para, detailRect);
	}

	public override Task OnEnter() {
		if (!_keysReady) {
			ConfigureKeys();
			_keysReady = true;
			keys.DumpBindings(nameof(InfoScreen));
		}
		return Task.CompletedTask;
	}

	public override string FooterMsg => "o open";

	public override string TitleMsg => "Info";

	private void ConfigureKeys() {
		keys.RegisterChar('o', "open in editor", KEY_OpenInEditor);
	}

	private bool KEY_OpenInEditor(ThaumTUI tui) {
		if (model.visibleSymbols.Count > 0) {
			CodeSymbol s = model.visibleSymbols.Selected;
			SysUtil.OpenInEditor(tui.projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
			return true;
		}
		return false;
	}
}