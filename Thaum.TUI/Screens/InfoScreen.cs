using Ratatui;
using Ratatui.Sugar;
using Thaum.Core.Crawling;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.Styles;

namespace Thaum.App.RatatuiTUI;

public sealed class InfoScreen : ThaumScreen {
	private bool _keysReady;

	public InfoScreen(ThaumTUI tui) : base(tui) { }

	public override void Draw(Terminal term, Rect area) {
		Paragraph title = Paragraph("", title: "Info", title_border: true);
		(Rect titleRect, Rect detailRect) = area.SplitTop(2);
		term.Draw(title, titleRect);
		if (model.visibleSymbols.Count == 0) return;
		CodeSymbol s    = model.visibleSymbols.Selected;
		Paragraph  para = Paragraph("");
		para.AppendSpan("Name: ", S_HINT).AppendSpan(s.Name, ThaumStyles.StyleForKind(s.Kind)).AppendLine("");
		para.AppendSpan("Kind: ", S_HINT).AppendSpan(s.Kind.ToString(), S_INFO).AppendLine("");
		para.AppendSpan("File: ", S_HINT).AppendSpan(s.FilePath, S_PATH).AppendLine("");
		para.AppendSpan("Start: ", S_HINT).AppendSpan($"L{s.StartCodeLoc.Line}", S_LINENUM).AppendSpan(":", S_HINT).AppendSpan($"C{s.StartCodeLoc.Character}", S_LINENUM).AppendLine("");
		para.AppendSpan("End:   ", S_HINT).AppendSpan($"L{s.EndCodeLoc.Line}", S_LINENUM).AppendSpan(":", S_HINT).AppendSpan($"C{s.EndCodeLoc.Character}", S_LINENUM).AppendLine("");
		para.AppendSpan("Children: ", S_HINT).AppendSpan((s.Children?.Count ?? 0).ToString(), S_INFO).AppendLine("");
		para.AppendSpan("Deps: ", S_HINT).AppendSpan((s.Dependencies?.Count ?? 0).ToString(), S_INFO).AppendLine("");
		para.AppendSpan("Last: ", S_HINT).AppendSpan((s.LastModified?.ToString("u") ?? "n/a"), S_INFO);
		term.Draw(para, detailRect);
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