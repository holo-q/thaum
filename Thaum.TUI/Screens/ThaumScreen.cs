using Ratatui;

namespace Thaum.App.RatatuiTUI;

public abstract class ThaumScreen : Screen<ThaumTUI> {
	protected ThaumModel model => tui.model;

	protected ThaumScreen(ThaumTUI tui) : base(tui) { }
}