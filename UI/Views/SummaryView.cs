using Terminal.Gui;
using Thaum.Core.Models;

namespace Thaum.UI.Views;

public class SummaryView : FrameView {
	private readonly TextView      _summaryText;
	private readonly Label         _symbolInfoLabel;
	private readonly ScrollBarView _scrollBar;
	private          CodeSymbol?   _currentSymbol;

	public SummaryView() : base("Summary") {
		// Symbol info at the top
		_symbolInfoLabel = new Label {
			X             = 1,
			Y             = 0,
			Width         = Dim.Fill(1),
			Height        = 2,
			TextAlignment = TextAlignment.Left,
			Text          = "No symbol selected"
		};

		// Summary text area
		_summaryText = new TextView {
			X        = 1,
			Y        = 2,
			Width    = Dim.Fill(2),
			Height   = Dim.Fill(1),
			ReadOnly = true,
			WordWrap = true,
			Text     = "Select a symbol to view its summary."
		};

		Add(_symbolInfoLabel, _summaryText);

		// Add scroll bar after adding to the view (Terminal.Gui v1.15.0 requirement)
		_scrollBar = new ScrollBarView(_summaryText, true);
		Add(_scrollBar);
	}

	public void UpdateSummary(CodeSymbol symbol) {
		_currentSymbol = symbol;

		// Update symbol info
		string symbolInfo = $"{GetSymbolIcon(symbol.Kind)} {symbol.Name} ({symbol.Kind})\n" +
		                    $"📁 {Path.GetFileName(symbol.FilePath)}:{symbol.StartPosition.Line}";

		if (symbol.LastModified.HasValue) {
			symbolInfo += $" | Modified: {symbol.LastModified:MM/dd HH:mm}";
		}

		_symbolInfoLabel.Text = symbolInfo;

		// Update summary content
		if (symbol.IsSummarized) {
			string summaryContent = $"Summary:\n{symbol.Summary}\n\n";

			if (symbol.HasExtractedKey) {
				summaryContent += $"Extracted Key: {symbol.ExtractedKey}\n\n";
			}

			if (symbol.Dependencies?.Any() == true) {
				summaryContent += $"Dependencies ({symbol.Dependencies.Count}):\n";
				foreach (string dep in symbol.Dependencies) {
					summaryContent += $"  • {dep}\n";
				}
				summaryContent += "\n";
			}

			if (symbol.Children?.Any() == true) {
				summaryContent += $"Child Symbols ({symbol.Children.Count}):\n";
				foreach (CodeSymbol child in symbol.Children) {
					string childStatus = child.IsSummarized ? "✓" : " ";
					summaryContent += $"  [{childStatus}] {GetSymbolIcon(child.Kind)} {child.Name}\n";
				}
			}

			_summaryText.Text = summaryContent;
		} else {
			_summaryText.Text = "This symbol has not been summarized yet.\n\n" +
			                    "Run the summarization process to generate a summary.";
		}
	}

	public void ClearSummary() {
		_currentSymbol        = null;
		_symbolInfoLabel.Text = "No symbol selected";
		_summaryText.Text     = "Select a symbol to view its summary.";
	}

	public CodeSymbol? GetCurrentSymbol() {
		return _currentSymbol;
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
}