using System.Text;
using Ratatui;

namespace Thaum.App.RatatuiTUI;

internal sealed class ReferencesScreen : IScreen
{
    public void Draw(Terminal term, Rect area, RatatuiApp.AppState app, string projectPath)
    {
        using var title = new Paragraph("References").Title("References", border: true);
        term.Draw(title, new Rect(area.X, area.Y, area.Width, 2));
        using var list = new Ratatui.List();
        var refs = app.refs ?? new List<(string File, int Line, string Name)>();
        int start = Math.Max(0, app.refsOffset);
        int end = Math.Min(refs.Count, start + Math.Max(1, area.Height - 2));
        for (int i = start; i < end; i++)
        {
            var (f, ln, nm) = refs[i];
            var lhs = Encoding.UTF8.GetBytes($"{Path.GetFileName(f)}:{ln}  ").AsMemory();
            var rhs = Encoding.UTF8.GetBytes(nm).AsMemory();
            ReadOnlyMemory<Ratatui.Batching.SpanRun> runs = new[]
            {
                new Ratatui.Batching.SpanRun(lhs, TuiTheme.FilePath),
                new Ratatui.Batching.SpanRun(rhs, default)
            };
            list.AppendItem(runs.Span);
        }
        term.Draw(list, new Rect(area.X, area.Y + 2, area.Width, area.Height - 2));
    }
}

