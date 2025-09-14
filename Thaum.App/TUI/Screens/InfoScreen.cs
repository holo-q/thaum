using Ratatui;
using Thaum.Core.Models;
using static Thaum.App.RatatuiTUI.Rat;
using static Thaum.App.RatatuiTUI.RatLayout;

namespace Thaum.App.RatatuiTUI;

internal sealed class InfoScreen : Screen
{
    private bool _keysReady;
    public InfoScreen(ThaumTUI tui, IEditorOpener opener, string projectPath)
        : base(tui, opener, projectPath) { }

    public override void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath)
    {
        using Paragraph title = Paragraph("", title: "Info", title_border: true);
        term.Draw(title, R(area.X, area.Y, area.Width, 2));
        if (app.visibleSymbols.Count == 0) return;
        CodeSymbol s = app.visibleSymbols[app.symSelected];
        using Paragraph para = Paragraph("");
        para.AppendSpan("Name: ", TuiTheme.Hint).AppendSpan(s.Name, TuiTheme.StyleForKind(s.Kind)).AppendLine("");
        para.AppendSpan("Kind: ", TuiTheme.Hint).AppendSpan(s.Kind.ToString(), TuiTheme.Info).AppendLine("");
        para.AppendSpan("File: ", TuiTheme.Hint).AppendSpan(s.FilePath, TuiTheme.FilePath).AppendLine("");
        para.AppendSpan("Start: ", TuiTheme.Hint).AppendSpan($"L{s.StartCodeLoc.Line}", TuiTheme.LineNumber).AppendSpan(":", TuiTheme.Hint).AppendSpan($"C{s.StartCodeLoc.Character}", TuiTheme.LineNumber).AppendLine("");
        para.AppendSpan("End:   ", TuiTheme.Hint).AppendSpan($"L{s.EndCodeLoc.Line}", TuiTheme.LineNumber).AppendSpan(":", TuiTheme.Hint).AppendSpan($"C{s.EndCodeLoc.Character}", TuiTheme.LineNumber).AppendLine("");
        para.AppendSpan("Children: ", TuiTheme.Hint).AppendSpan((s.Children?.Count ?? 0).ToString(), TuiTheme.Info).AppendLine("");
        para.AppendSpan("Deps: ", TuiTheme.Hint).AppendSpan((s.Dependencies?.Count ?? 0).ToString(), TuiTheme.Info).AppendLine("");
        para.AppendSpan("Last: ", TuiTheme.Hint).AppendSpan((s.LastModified?.ToString("u") ?? "n/a"), TuiTheme.Info);
        term.Draw(para, R(area.X, area.Y + 2, area.Width, area.Height - 2));
    }

    public override Task OnEnter(ThaumTUI.State app)
    {
        if (!_keysReady) { ConfigureKeys(); _keysReady = true; }
        return Task.CompletedTask;
    }

    public override string FooterHint(ThaumTUI.State app) => "o open";

    public override string Title(ThaumTUI.State app) => "Info";

    private void ConfigureKeys()
    {
        ConfigureDefaultGlobalKeys();
        keys.RegisterChar('o', "open in editor", KEY_OpenInEditor);
    }

    private bool KEY_OpenInEditor(ThaumTUI.State a)
    {
        if (a.visibleSymbols.Count > 0) {
            CodeSymbol s = a.visibleSymbols[a.symSelected];
            opener.Open(projectPath, s.FilePath, Math.Max(1, s.StartCodeLoc.Line));
            return true;
        }
        return false;
    }
}
