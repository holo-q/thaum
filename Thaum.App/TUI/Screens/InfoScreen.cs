using Ratatui;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

internal sealed class InfoScreen : IScreen
{
    public void Draw(Terminal term, Rect area, RatatuiApp.AppState app, string projectPath)
    {
        using var title = new Paragraph("Info").Title("Info", border: true);
        term.Draw(title, new Rect(area.X, area.Y, area.Width, 2));
        if (app.visibleSymbols.Count == 0) return;
        CodeSymbol s = app.visibleSymbols[app.symSelected];
        using var para = new Paragraph("");
        para.AppendSpan("Name: ", TuiTheme.Hint).AppendSpan(s.Name, TuiTheme.StyleForKind(s.Kind)).AppendLine("");
        para.AppendSpan("Kind: ", TuiTheme.Hint).AppendSpan(s.Kind.ToString(), TuiTheme.Info).AppendLine("");
        para.AppendSpan("File: ", TuiTheme.Hint).AppendSpan(s.FilePath, TuiTheme.FilePath).AppendLine("");
        para.AppendSpan("Start: ", TuiTheme.Hint).AppendSpan($"L{s.StartCodeLoc.Line}", TuiTheme.LineNumber).AppendSpan(":", TuiTheme.Hint).AppendSpan($"C{s.StartCodeLoc.Character}", TuiTheme.LineNumber).AppendLine("");
        para.AppendSpan("End:   ", TuiTheme.Hint).AppendSpan($"L{s.EndCodeLoc.Line}", TuiTheme.LineNumber).AppendSpan(":", TuiTheme.Hint).AppendSpan($"C{s.EndCodeLoc.Character}", TuiTheme.LineNumber).AppendLine("");
        para.AppendSpan("Children: ", TuiTheme.Hint).AppendSpan((s.Children?.Count ?? 0).ToString(), TuiTheme.Info).AppendLine("");
        para.AppendSpan("Deps: ", TuiTheme.Hint).AppendSpan((s.Dependencies?.Count ?? 0).ToString(), TuiTheme.Info).AppendLine("");
        para.AppendSpan("Last: ", TuiTheme.Hint).AppendSpan((s.LastModified?.ToString("u") ?? "n/a"), TuiTheme.Info);
        term.Draw(para, new Rect(area.X, area.Y + 2, area.Width, area.Height - 2));
    }
}

