using Ratatui;
using Ratatui.Layout;
using System.Text;

namespace Thaum.App.RatatuiTUI;

internal sealed class BrowserScreen : IScreen
{
    public void Draw(Terminal term, Rect area, RatatuiApp.AppState app, string projectPath)
    {
        var cols = Layout.SplitVertical(area, new[]
        {
            Constraint.Percentage(30),
            Constraint.Percentage(30),
            Constraint.Percentage(40)
        }, gap: 1, margin: 0);

        using (var title = new Paragraph(app.fileFilter.Length == 0 ? "(type to filter)" : $"/{app.fileFilter}").Title("Files", border: true))
            term.Draw(title, new Rect(cols[0].X, cols[0].Y, cols[0].Width, 2));

        using (var files = new Ratatui.List().Title("Files"))
        {
            foreach (var f in app.visibleFiles)
                files.AppendItem(FileLine(projectPath, f), TuiTheme.FilePath);
            using var fs = new Ratatui.ListState().Selected(app.fileSelected).Offset(app.fileOffset);
            term.Draw(files, new Rect(cols[0].X, cols[0].Y + 2, cols[0].Width, cols[0].Height - 2), fs);
        }

        using (var title = new Paragraph(app.symFilter.Length == 0 ? "(type to filter)" : $"/{app.symFilter}").Title("Symbols", border: true))
            term.Draw(title, new Rect(cols[1].X, cols[1].Y, cols[1].Width, 2));

        using (var syms = new Ratatui.List().Title("Symbols"))
        {
            foreach (var s in app.visibleSymbols)
                syms.AppendItem(SymbolLine(s), TuiTheme.StyleForKind(s.Kind));
            using var ss = new Ratatui.ListState().Selected(app.symSelected).Offset(app.symOffset);
            term.Draw(syms, new Rect(cols[1].X, cols[1].Y + 2, cols[1].Width, cols[1].Height - 2), ss);
        }

        bool hasSym = app.visibleSymbols.Count > 0;
        using var meta = new Paragraph("").Title("Details", border: true)
            .AppendLine(!hasSym ? "" : $"Name: {app.visibleSymbols[app.symSelected].Name}")
            .AppendLine(!hasSym ? "" : $"Kind: {app.visibleSymbols[app.symSelected].Kind}")
            .AppendLine(!hasSym ? "" : $"File: {app.visibleSymbols[app.symSelected].FilePath}")
            .AppendLine(!hasSym ? "" : $"Span: L{app.visibleSymbols[app.symSelected].StartCodeLoc.Line}:C{app.visibleSymbols[app.symSelected].StartCodeLoc.Character}");

        var right = cols[2];
        int metaHeight = Math.Max(6, right.Height / 3);
        term.Draw(meta, new Rect(right.X, right.Y, right.Width, metaHeight));
        string body = app.isLoading ? $"Summarizing… {TuiTheme.Spinner()}" : (app.summary ?? "");
        using var detail = new Paragraph("").Title("Summary", border: true);
        if (body.StartsWith("Error:")) detail.AppendSpan(body, TuiTheme.Error); else detail.AppendSpan(body);
        term.Draw(detail, new Rect(right.X, right.Y + metaHeight + 1, right.Width, Math.Max(1, right.Height - metaHeight - 1)));
    }

    private static string FileLine(string projectPath, string path)
    {
        try { return Path.GetRelativePath(projectPath, path); }
        catch { return Path.GetFileName(path); }
    }

    private static string SymbolLine(Thaum.Core.Models.CodeSymbol s)
        => $"{(s.Kind switch { Thaum.Core.Models.SymbolKind.Class => "[C]", Thaum.Core.Models.SymbolKind.Method => "[M]", Thaum.Core.Models.SymbolKind.Function => "[F]", Thaum.Core.Models.SymbolKind.Interface => "[I]", Thaum.Core.Models.SymbolKind.Enum => "[E]", _ => "[·]" })} {s.Name.Replace('\n', ' ')}";
}

