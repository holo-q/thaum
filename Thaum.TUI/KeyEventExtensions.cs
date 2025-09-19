using Ratatui;

namespace Thaum.App.RatatuiTUI;

internal static class KeyEventExtensions {
	public static bool IsEscape(this KeyEvent key)
		=> key.CodeEnum == KeyCode.ESC;

	public static bool IsChar(this KeyEvent key, char ch, bool ignoreCase = false) {
		if (key.CodeEnum != KeyCode.Char) return false;
		char current = (char)key.Char;
		return ignoreCase
			? char.ToUpperInvariant(current) == char.ToUpperInvariant(ch)
			: current == ch;
	}

	public static bool IsCtrlChar(this KeyEvent key, char ch, bool ignoreCase = false)
		=> key.Ctrl && key.IsChar(ch, ignoreCase);

	public static bool IsLetter(this KeyEvent key, char ch)
		=> key.IsChar(ch, ignoreCase: true);

	public static bool IsCtrlLetter(this KeyEvent key, char ch)
		=> key.IsCtrlChar(ch, ignoreCase: true);
}
