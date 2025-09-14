using Ratatui;

namespace Thaum.App.RatatuiTUI;

internal sealed class SummaryScreen : IScreen
{
    public void Draw(Terminal term, Rect area, RatatuiApp.AppState app, string projectPath)
    {
        using var title = new Paragraph("Summary").Title("Summary", border: true);
        term.Draw(title, new Rect(area.X, area.Y, area.Width, 2));
        string bodyText = app.isLoading ? $"Summarizing… {TuiTheme.Spinner()}" : (app.summary ?? "No summary yet. Press 3 to (re)generate.");
        using var para = new Paragraph("");
        if (bodyText.StartsWith("Error:")) para.AppendSpan(bodyText, TuiTheme.Error); else para.AppendSpan(bodyText);
        term.Draw(para, new Rect(area.X, area.Y + 2, area.Width, area.Height - 2));
    }
}

