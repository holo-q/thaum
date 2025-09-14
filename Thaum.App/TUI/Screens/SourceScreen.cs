using System.Text;
using Ratatui;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

internal sealed class SourceScreen : IScreen
{
    public void Draw(Terminal term, Rect area, RatatuiApp.AppState app, string projectPath)
    {
        using var title = new Ratatui.Paragraph("Source").Title("Source", border: true);
        term.Draw(title, new Rect(area.X, area.Y, area.Width, 2));

        var lines = app.sourceLines ?? new List<string>();
        using var list = new Ratatui.List();
        int start = Math.Max(0, app.sourceOffset);
        int end = Math.Min(lines.Count, start + Math.Max(1, area.Height - 3));

        int symStartLine = 0, symEndLine = -1, symStartCol = 0, symEndCol = 0;
        if (app.visibleSymbols.Count > 0)
        {
            CodeSymbol s = app.visibleSymbols[app.symSelected];
            symStartLine = Math.Max(1, s.StartCodeLoc.Line);
            symEndLine = Math.Max(symStartLine, s.EndCodeLoc.Line);
            symStartCol = Math.Max(0, s.StartCodeLoc.Character);
            symEndCol = Math.Max(symStartCol, s.EndCodeLoc.Character);
        }

        for (int i = start; i < end; i++)
        {
            string num = (i + 1).ToString().PadLeft(5) + "  ";
            var ln = Encoding.UTF8.GetBytes(num).AsMemory();
            string line = lines[i];
            var runs = new List<Ratatui.Batching.SpanRun>(4) { new Ratatui.Batching.SpanRun(ln, TuiTheme.LineNumber) };
            int oneBased = i + 1;
            if (oneBased >= symStartLine && oneBased <= symEndLine)
            {
                int sc = (oneBased == symStartLine) ? symStartCol : 0;
                int ec = (oneBased == symEndLine) ? symEndCol : line.Length;
                sc = Math.Clamp(sc, 0, line.Length);
                ec = Math.Clamp(ec, sc, line.Length);
                string pre = line[..sc], mid = line[sc..ec], post = line[ec..];
                if (pre.Length > 0) runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(pre).AsMemory(), default));
                if (mid.Length > 0) runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(mid).AsMemory(), TuiTheme.CodeHi));
                if (post.Length > 0) runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(post).AsMemory(), default));
            }
            else
            {
                runs.Add(new Ratatui.Batching.SpanRun(Encoding.UTF8.GetBytes(line).AsMemory(), default));
            }
            list.AppendItem(CollectionsMarshal.AsSpan(runs));
        }
        term.Draw(list, new Rect(area.X, area.Y + 2, area.Width, area.Height - 2));
    }
}

