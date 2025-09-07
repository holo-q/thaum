using System.Collections.ObjectModel;
using Terminal.Gui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Thaum.Core.Models;
using Thaum.TUI;

namespace Thaum.UI.Views;

public class SymbolListView : FrameView {
	private readonly ListView         _listView;
	private readonly List<CodeSymbol> _symbols = new();

	public event Action<CodeSymbol?>? SelectionChanged;

	public SymbolListView() : base() {
		Title = "Symbols";
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
		var items = new ObservableCollection<string>(_symbols.Select(FormatSymbolItem));
		_listView.SetSource(items);

		if (items.Count > 0) {
			_listView.SelectedItem = 0;
		}
	}

	private string FormatSymbolItem(CodeSymbol symbol) {
		string icon   = IconProvider.GetSymbolKindIcon(symbol.Kind);
		string status = IconProvider.GetSymbolStatusIcon(symbol);

		return $"{icon} {symbol.Name} {status}";
	}


	private void OnSelectionChanged(object? sender, ListViewItemEventArgs args) {
		int selectedItem = _listView.SelectedItem;
		if (selectedItem >= 0 && selectedItem < _symbols.Count) {
			SelectionChanged?.Invoke(_symbols[selectedItem]);
		} else {
			SelectionChanged?.Invoke(null);
		}
	}
}