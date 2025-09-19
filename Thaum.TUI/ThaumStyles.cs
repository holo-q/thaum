using Ratatui;
using Thaum.Core.Crawling;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

public partial class ThaumStyles {
	public static Style StyleForKind(SymbolKind k) => k switch {
		SymbolKind.Class     => new Style(fg: Color.LightYellow),
		SymbolKind.Method    => new Style(fg: Color.LightGreen),
		SymbolKind.Function  => new Style(fg: Color.LightGreen),
		SymbolKind.Interface => new Style(fg: Color.LightBlue),
		SymbolKind.Enum      => new Style(fg: Color.Magenta),
		SymbolKind.Property  => new Style(fg: Color.White),
		SymbolKind.Field     => new Style(fg: Color.White),
		SymbolKind.Variable  => new Style(fg: Color.White),
		_                    => new Style(fg: Color.Gray)
	};
}