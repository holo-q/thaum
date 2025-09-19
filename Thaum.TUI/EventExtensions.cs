using Ratatui;

namespace Thaum.App.RatatuiTUI;

internal static class EventExtensions {
	public static Vec2 Size(this Event ev)
		=> new Vec2(ev.Width, ev.Height);
}
