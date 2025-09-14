using Ratatui;

namespace Thaum.App.RatatuiTUI;

internal interface IScreen
{
    void Draw(Terminal term, Rect area, RatatuiApp.AppState app, string projectPath);
}
