using Ratatui;
using Thaum.Core.Crawling;

namespace Thaum.App.RatatuiTUI;

public partial class ThaumStyles {
	public static Style StyleForKind(SymbolKind k) => k switch {
		SymbolKind.Class     => new Style(fg: Col.LYELLOW),
		SymbolKind.Method    => new Style(fg: Col.LIGHTGREEN),
		SymbolKind.Function  => new Style(fg: Col.LIGHTGREEN),
		SymbolKind.Interface => new Style(fg: Col.LBLUE),
		SymbolKind.Enum      => new Style(fg: Col.MAGENTA),
		SymbolKind.Property  => new Style(fg: Col.WHITE),
		SymbolKind.Field     => new Style(fg: Col.WHITE),
		SymbolKind.Variable  => new Style(fg: Col.WHITE),
		_                    => new Style(fg: Col.GRAY)
	};
}