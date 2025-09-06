using Terminal.Gui;
using Thaum.Core.Models;

namespace Thaum.UI.Views;

public class SymbolListView : FrameView {
	private readonly ListView         _listView;
	private readonly List<CodeSymbol> _symbols = new();

	public event Action<CodeSymbol?>? SelectionChanged;

	public SymbolListView() : base("Symbols") {
		_listView = new ListView {
			X             = 0,
			Y             = 0,
			Width         = Dim.Fill(),
			Height        = Dim.Fill(),
			AllowsMarking = false
		};

		_listView.OpenSelectedItem += OnSelectionChanged;

		Add(_listView);
	}

	public void UpdateSymbols(List<CodeSymbol> symbols) {
		_symbols.Clear();
		_symbols.AddRange(symbols);
		RefreshList();
	}

	public void UpdateHierarchy(SymbolHierarchy hierarchy) {
		_symbols.Clear();
		AddSymbolsRecursively(hierarchy.RootSymbols);
		RefreshList();
	}

	public void ClearSymbols() {
		_symbols.Clear();
		RefreshList();
	}

	private void AddSymbolsRecursively(List<CodeSymbol> symbols, int indent = 0) {
		foreach (CodeSymbol symbol in symbols.OrderBy(s => s.StartCodeLoc.Line)) {
			// Add indentation for nested symbols
			CodeSymbol displaySymbol = indent > 0 ? symbol with { Name = new string(' ', indent * 2) + symbol.Name } : symbol;

			_symbols.Add(displaySymbol);

			if (symbol.Children?.Any() == true) {
				AddSymbolsRecursively(symbol.Children, indent + 1);
			}
		}
	}

	private void RefreshList() {
		string[] items = _symbols.Select(FormatSymbolItem).ToArray();
		_listView.SetSource(items);

		if (items.Length > 0) {
			_listView.SelectedItem = 0;
		}
	}

	private string FormatSymbolItem(CodeSymbol symbol) {
		string icon   = GetSymbolIcon(symbol.Kind);
		string status = GetSymbolStatus(symbol);

		return $"{icon} {symbol.Name} {status}";
	}

	private static string GetSymbolIcon(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "ƒ",
			SymbolKind.Method    => "ƒ",
			SymbolKind.Class     => "C",
			SymbolKind.Interface => "I",
			SymbolKind.Module    => "M",
			SymbolKind.Namespace => "N",
			SymbolKind.Property  => "P",
			SymbolKind.Field     => "F",
			SymbolKind.Variable  => "V",
			SymbolKind.Parameter => "p",
			_                    => "?"
		};
	}

	private static string GetSymbolStatus(CodeSymbol symbol) {
		if (symbol.IsSummarized) {
			return symbol.HasExtractedKey ? "[✓K]" : "[✓]";
		}
		return "[ ]";
	}

	private void OnSelectionChanged(EventArgs args) {
		int selectedItem = _listView.SelectedItem;
		if (selectedItem >= 0 && selectedItem < _symbols.Count) {
			SelectionChanged?.Invoke(_symbols[selectedItem]);
		} else {
			SelectionChanged?.Invoke(null);
		}
	}
}