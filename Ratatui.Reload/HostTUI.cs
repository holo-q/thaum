using Thaum.App.RatatuiTUI;
using static Thaum.App.RatatuiTUI.Rat;

namespace Ratatui.Reload;

/// <summary>
/// Fallback TUI that shows host status, error messages, and loading states.
/// Always available when no plugin TUI can be loaded.
/// This should be a fancy thing with some ascii art monument in the middle,
/// visual effects, etc...
/// </summary>
public class HostTUI : RatTUI<HostTUI> {
	private string _message = "Initializing...";
	private bool   _isError = false;

	public void SetMessage(string message, bool isError = false) {
		_message = message;
		_isError = isError;
		Invalidate();
	}

	public void SetLoading(string operation) {
		_message = $"Loading: {operation}";
		_isError = false;
		Invalidate();
	}

	public void SetError(string error) {
		_message = $"Error: {error}";
		_isError = true;
		Invalidate();
	}

	public override void OnDraw(Terminal term) {
		(int w, int h) = term.Size();

		// Center the message
		int mw = min(_message.Length + 4, w - 2);
		int mh = 3;
		int x  = (w - mw) / 2;
		int y  = (h - mh) / 2;

		Rect   rect   = rect_sz(x, y, mw, mh);
		Colors colors = _isError ? Colors.LIGHTRED : Colors.LBLUE;

		using var para = new Paragraph(_message)
			.Title("Thaum Host", border: true)
			.Style(new Style(fg: colors));

		term.Draw(para, rect);
	}

	public override bool OnEvent(Event ev) {
		// Host TUI can handle basic events like quit
		if (ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char }) {
			char ch = (char)ev.Key.Char;
			if (ch is 'q' or 'Q') {
				// Signal quit
				return true;
			}
		}
		return false;
	}
}