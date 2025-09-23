using Ratatui;
using Thaum.Core.Crawling;

namespace Thaum.App.RatatuiTUI;

public partial class ThaumStyles {
	public static Style StyleForKind(SymbolKind k) => k switch {
		SymbolKind.Class     => new Style(fg: Colors.LYELLOW),
		SymbolKind.Method    => new Style(fg: Colors.LIGHTGREEN),
		SymbolKind.Function  => new Style(fg: Colors.LIGHTGREEN),
		SymbolKind.Interface => new Style(fg: Colors.LBLUE),
		SymbolKind.Enum      => new Style(fg: Colors.MAGENTA),
		SymbolKind.Property  => new Style(fg: Colors.WHITE),
		SymbolKind.Field     => new Style(fg: Colors.WHITE),
		SymbolKind.Variable  => new Style(fg: Colors.WHITE),
		_                    => new Style(fg: Colors.GRAY)
	};
}