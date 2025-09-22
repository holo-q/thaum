using Ratatui;
using Thaum.Core.Crawling;

namespace Thaum.App.RatatuiTUI;

public partial class ThaumStyles {
	public static Style StyleForKind(SymbolKind k) => k switch {
		SymbolKind.Class     => new Style(fg: Colors.LightYellow),
		SymbolKind.Method    => new Style(fg: Colors.LightGreen),
		SymbolKind.Function  => new Style(fg: Colors.LightGreen),
		SymbolKind.Interface => new Style(fg: Colors.LightBlue),
		SymbolKind.Enum      => new Style(fg: Colors.Magenta),
		SymbolKind.Property  => new Style(fg: Colors.White),
		SymbolKind.Field     => new Style(fg: Colors.White),
		SymbolKind.Variable  => new Style(fg: Colors.White),
		_                    => new Style(fg: Colors.Gray)
	};
}