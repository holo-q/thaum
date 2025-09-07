using System.Drawing;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Input;
using TreeNode = Thaum.CLI.Models.TreeNode; // Resolve naming conflict
using Thaum.CLI.Models;
using Thaum.TUI.Models;
using Thaum.Core.Models;

namespace Thaum.TUI.Views;

/// <summary>
/// Custom view component that renders symbols in a grid-like layout with individual symbol
/// selection capabilities where each symbol can be independently selected using keyboard
/// navigation or mouse clicks enabling precise symbol-level interaction in compact mode
/// </summary>
public class SymbolGridView : View {
	private readonly BrowserState _state;
	private readonly List<SymbolPosition> _symbolPositions = [];
	private int _selectedSymbolIndex = -1;
	
	public event EventHandler<SymbolSelectedEventArgs>? SymbolSelected;
	
	public SymbolGridView(BrowserState state) {
		_state = state;
		
		// Enable focus for keyboard events
		CanFocus = true;
		
		// Use terminal's native color scheme (v2: Set automatically)
	}
	
	/// <summary>
	/// Updates the symbol grid with new display nodes and rebuilds position map
	/// </summary>
	public void UpdateNodes(List<TreeNode> nodes) {
		_state.DisplayNodes = nodes;
		RebuildSymbolPositions();
		SetNeedsDraw();
	}
	
	/// <summary>
	/// Sets the selected symbol by index in the position array
	/// </summary>
	public void SetSelectedSymbol(int symbolIndex) {
		if (symbolIndex >= 0 && symbolIndex < _symbolPositions.Count) {
			_selectedSymbolIndex = symbolIndex;
			SetNeedsDraw();
		}
	}
	
	/// <summary>
	/// Gets the currently selected symbol, if any
	/// </summary>
	public CodeSymbol? GetSelectedSymbol() {
		return _selectedSymbolIndex >= 0 && _selectedSymbolIndex < _symbolPositions.Count
			? _symbolPositions[_selectedSymbolIndex].Symbol
			: null;
	}
	
	/// <summary>
	/// Rebuilds the symbol position map based on current state and view mode
	/// </summary>
	private void RebuildSymbolPositions() {
		_symbolPositions.Clear();
		
		if (!_state.CompactMode) {
			// In expanded mode, each symbol gets its own line - simpler case
			RebuildExpandedPositions();
		} else {
			// In compact mode, multiple symbols per line - complex layout
			RebuildCompactPositions();
		}
		
		// Validate selection after rebuild
		if (_selectedSymbolIndex >= _symbolPositions.Count) {
			_selectedSymbolIndex = _symbolPositions.Count > 0 ? 0 : -1;
		}
	}
	
	/// <summary>
	/// Builds symbol positions for expanded mode (one symbol per line)
	/// </summary>
	private void RebuildExpandedPositions() {
		int currentRow = 0;
		
		foreach (var fileNode in _state.DisplayNodes) {
			// Skip file header row
			currentRow++;
			
			// Add each symbol on its own line
			foreach (var child in fileNode.Children.OrderBy(c => c.Name)) {
				var icon = IconProvider.GetSymbolKindIcon(child.Kind);
				var prefix = $"    ├── {icon} ";
				var startCol = prefix.Length;
				
				_symbolPositions.Add(new SymbolPosition(child.Symbol, currentRow, startCol));
				currentRow++;
			}
		}
	}
	
	/// <summary>
	/// Builds symbol positions for compact mode (multiple symbols per line)
	/// </summary>
	private void RebuildCompactPositions() {
		int currentRow = 0;
		
		foreach (var fileNode in _state.DisplayNodes) {
			// Skip file header row
			currentRow++;
			
			if (fileNode.Children.Any()) {
				// Group children by symbol kind
				var groupedChildren = fileNode.Children.GroupBy(c => c.Kind).ToList();
				
				foreach (var group in groupedChildren) {
					var symbols = group.ToList();
					var kindName = GetKindDisplayName(group.Key);
					var icon = IconProvider.GetSymbolKindIcon(group.Key);
					
					// Calculate positions for symbols in this group
					var prefix = $"    ├── {icon} {kindName}: ";
					int currentCol = prefix.Length;
					
					for (int i = 0; i < symbols.Count && i < 5; i++) { // Limit to 5 visible
						var symbol = symbols[i];
						_symbolPositions.Add(new SymbolPosition(symbol.Symbol, currentRow, currentCol));
						
						// Move to next position (symbol name + space)
						currentCol += symbol.Symbol.Name.Length;
						if (i < symbols.Count - 1 && i < 4) { // Add space except after last symbol
							currentCol += 1;
						}
					}
					
					currentRow++;
				}
			}
		}
	}
	
	/// <summary>
	/// Custom redraw implementation that renders symbols with selection highlighting
	/// </summary>
	// v2 API: Override OnDrawingContent instead of Redraw
	protected override bool OnDrawingContent() {
		// Clear the view
		SetAttribute(GetAttributeForRole(VisualRole.Normal));
		for (int y = 0; y < Viewport.Height; y++) {
			Move(0, y);
			Application.Driver?.AddStr(new string(' ', Viewport.Width));
		}
		
		// Render content based on mode
		if (!_state.CompactMode) {
			RenderExpandedMode(Viewport);
		} else {
			RenderCompactMode(Viewport);
		}
		
		return true; // Indicate drawing was handled
	}
	
	/// <summary>
	/// Renders symbols in expanded mode with selection highlighting
	/// </summary>
	private void RenderExpandedMode(Rectangle bounds) {
		int currentRow = 0;
		
		foreach (var fileNode in _state.DisplayNodes) {
			if (currentRow >= bounds.Height) break;
			
			// Render file header
			Move(0, currentRow);
			SetAttribute(GetAttributeForRole(VisualRole.Normal));
			Application.Driver?.AddStr($"└── [DIR] {fileNode.Name}");
			currentRow++;
			
			// Render symbols
			foreach (var child in fileNode.Children.OrderBy(c => c.Name)) {
				if (currentRow >= bounds.Height) break;
				
				var icon = IconProvider.GetSymbolKindIcon(child.Kind);
				var symbolPos = _symbolPositions.FirstOrDefault(p => 
					p.Symbol == child.Symbol && p.Row == currentRow);
				var isSelected = symbolPos != null && 
					_symbolPositions.IndexOf(symbolPos) == _selectedSymbolIndex;
				
				Move(0, currentRow);
				SetAttribute(GetAttributeForRole(VisualRole.Normal));
				Application.Driver?.AddStr($"    ├── {icon} ");
				
				// Render symbol name with selection highlighting
				if (isSelected) {
					SetAttribute(GetAttributeForRole(VisualRole.Active));
				}
				Application.Driver?.AddStr(child.Symbol.Name);
				if (isSelected) {
					SetAttribute(GetAttributeForRole(VisualRole.Normal));
				}
				
				currentRow++;
			}
		}
	}
	
	/// <summary>
	/// Renders symbols in compact mode with individual symbol selection highlighting
	/// </summary>
	private void RenderCompactMode(Rectangle bounds) {
		int currentRow = 0;
		
		foreach (var fileNode in _state.DisplayNodes) {
			if (currentRow >= bounds.Height) break;
			
			// Render file header
			Move(0, currentRow);
			SetAttribute(GetAttributeForRole(VisualRole.Normal));
			Application.Driver?.AddStr($"└── [DIR] {fileNode.Name}");
			currentRow++;
			
			if (fileNode.Children.Any()) {
				// Group children by symbol kind
				var groupedChildren = fileNode.Children.GroupBy(c => c.Kind).ToList();
				
				foreach (var group in groupedChildren) {
					if (currentRow >= bounds.Height) break;
					
					var symbols = group.ToList();
					var kindName = GetKindDisplayName(group.Key);
					var icon = IconProvider.GetSymbolKindIcon(group.Key);
					
					Move(0, currentRow);
					SetAttribute(GetAttributeForRole(VisualRole.Normal));
					Application.Driver?.AddStr($"    ├── {icon} {kindName}: ");
					
					// Render symbols with individual highlighting
					for (int i = 0; i < symbols.Count && i < 5; i++) {
						var symbol = symbols[i].Symbol;
						var symbolPos = _symbolPositions.FirstOrDefault(p => 
							p.Symbol == symbol && p.Row == currentRow);
						var isSelected = symbolPos != null && 
							_symbolPositions.IndexOf(symbolPos) == _selectedSymbolIndex;
						
						if (isSelected) {
							SetAttribute(GetAttributeForRole(VisualRole.Active));
						}
						Application.Driver?.AddStr(symbol.Name);
						if (isSelected) {
							SetAttribute(GetAttributeForRole(VisualRole.Normal));
						}
						
						// Add space between symbols
						if (i < symbols.Count - 1 && i < 4) {
							Application.Driver?.AddStr(" ");
						}
					}
					
					// Show remaining count
					if (symbols.Count > 5) {
						Application.Driver?.AddStr($" +{symbols.Count - 5}");
					}
					
					currentRow++;
				}
			}
		}
	}
	
	/// <summary>
	/// Handles keyboard navigation between symbols
	/// </summary>
	// v2 API: Override OnKeyDown instead of ProcessKey
	protected override bool OnKeyDown(Key key) {
		switch (key.KeyCode) {
			case KeyCode.CursorLeft:
				NavigateHorizontal(-1);
				return true;
			case KeyCode.CursorRight:
				NavigateHorizontal(1);
				return true;
			case KeyCode.CursorUp:
				NavigateVertical(-1);
				return true;
			case KeyCode.CursorDown:
				NavigateVertical(1);
				return true;
			case KeyCode.Enter:
				if (GetSelectedSymbol() != null) {
					SymbolSelected?.Invoke(this, new SymbolSelectedEventArgs(GetSelectedSymbol()!));
				}
				return true;
		}
		
		return base.OnKeyDown(key);
	}
	
	/// <summary>
	/// Handles mouse clicks for symbol selection
	/// </summary>
	// v2 API: Override OnMouseEvent instead of ProcessMouse
	protected override bool OnMouseEvent(MouseEventArgs mouseEvent) {
		if (mouseEvent.Flags.HasFlag(MouseFlags.Button1Clicked)) {
			var clickedSymbol = FindSymbolAtPosition(mouseEvent.Position.Y, mouseEvent.Position.X);
			if (clickedSymbol != -1) {
				SetSelectedSymbol(clickedSymbol);
				return true;
			}
		}
		
		return base.OnMouseEvent(mouseEvent);
	}
	
	/// <summary>
	/// Navigates horizontally between symbols on the same line
	/// </summary>
	private void NavigateHorizontal(int direction) {
		if (_selectedSymbolIndex == -1 || _symbolPositions.Count == 0) return;
		
		var currentPos = _symbolPositions[_selectedSymbolIndex];
		var sameLine = _symbolPositions.Where(p => p.Row == currentPos.Row).OrderBy(p => p.StartCol).ToList();
		var currentIndex = sameLine.FindIndex(p => p == currentPos);
		
		if (currentIndex != -1) {
			var newIndex = currentIndex + direction;
			if (newIndex >= 0 && newIndex < sameLine.Count) {
				var newPos = sameLine[newIndex];
				_selectedSymbolIndex = _symbolPositions.IndexOf(newPos);
				SetNeedsDraw();
			}
		}
	}
	
	/// <summary>
	/// Navigates vertically between symbols, maintaining horizontal position when possible
	/// </summary>
	private void NavigateVertical(int direction) {
		if (_selectedSymbolIndex == -1 || _symbolPositions.Count == 0) return;
		
		var currentPos = _symbolPositions[_selectedSymbolIndex];
		var targetRow = currentPos.Row + direction;
		
		// Find symbols on target row
		var targetRowSymbols = _symbolPositions.Where(p => p.Row == targetRow).OrderBy(p => p.StartCol).ToList();
		
		if (targetRowSymbols.Any()) {
			// Find closest symbol to current horizontal position
			var closestSymbol = targetRowSymbols
				.OrderBy(p => Math.Abs(p.StartCol - currentPos.StartCol))
				.First();
			
			_selectedSymbolIndex = _symbolPositions.IndexOf(closestSymbol);
			SetNeedsDraw();
		}
	}
	
	/// <summary>
	/// Finds the symbol at a specific screen position
	/// </summary>
	private int FindSymbolAtPosition(int row, int col) {
		for (int i = 0; i < _symbolPositions.Count; i++) {
			if (_symbolPositions[i].Contains(row, col)) {
				return i;
			}
		}
		return -1;
	}
	
	
	private static string GetKindDisplayName(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function => "functions",
			SymbolKind.Method => "methods",
			SymbolKind.Class => "classes",
			SymbolKind.Interface => "interfaces",
			SymbolKind.Property => "properties",
			SymbolKind.Field => "fields",
			SymbolKind.Variable => "variables",
			SymbolKind.Enum => "enums",
			SymbolKind.EnumMember => "enum members",
			SymbolKind.Constructor => "constructors",
			SymbolKind.Namespace => "namespaces",
			_ => kind.ToString().ToLowerInvariant()
		};
	}
}

/// <summary>
/// Event arguments for symbol selection events
/// </summary>
public class SymbolSelectedEventArgs : EventArgs {
	public CodeSymbol Symbol { get; }
	
	public SymbolSelectedEventArgs(CodeSymbol symbol) {
		Symbol = symbol;
	}
}