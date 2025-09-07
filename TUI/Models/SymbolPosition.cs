using Thaum.Core.Models;

namespace Thaum.TUI.Models;

/// <summary>
/// Represents the screen position and metadata for a selectable symbol in compact mode
/// where each symbol occupies a specific rectangular region on the display that can
/// be individually selected and highlighted for precise user interaction
/// </summary>
public class SymbolPosition {
	public CodeSymbol Symbol { get; set; }
	public int Row { get; set; }
	public int StartCol { get; set; }
	public int EndCol { get; set; }
	public int Length => EndCol - StartCol;
	
	/// <summary>
	/// Creates a new symbol position with calculated end column
	/// </summary>
	public SymbolPosition(CodeSymbol symbol, int row, int startCol) {
		Symbol = symbol;
		Row = row;
		StartCol = startCol;
		EndCol = startCol + symbol.Name.Length;
	}
	
	/// <summary>
	/// Checks if a screen coordinate falls within this symbol's bounds
	/// </summary>
	public bool Contains(int row, int col) {
		return Row == row && col >= StartCol && col < EndCol;
	}
	
	/// <summary>
	/// Gets the display text for this symbol
	/// </summary>
	public string DisplayText => Symbol.Name;
}