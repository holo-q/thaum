using Terminal.Gui;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Thaum.TUI.Models;

namespace Thaum.TUI.Views;

/// <summary>
/// Status bar displaying current view mode navigation context and available keyboard shortcuts
/// where the format follows "MAP > COMPRESS    BKSP/ESC to back out    R to retry" pattern
/// providing users with contextual guidance for available actions and navigation state
/// </summary>
public class StatusBarView : View {
	private readonly BrowserState _state;
	private Label? _statusLabel;
	
	public StatusBarView(BrowserState state) {
		_state = state;
		InitializeComponents();
	}
	
	private void InitializeComponents() {
		// Force terminal colors for proper transparency
		ColorScheme = Colors.ColorSchemes["Base"];
		
		_statusLabel = new Label {
			X = 1,
			Y = 0,
			Width = Dim.Fill(1),
			Height = 1,
			Text = "",
			TextAlignment = Alignment.Start,
			ColorScheme = Colors.ColorSchemes["Base"]
		};
		
		Add(_statusLabel);
	}
	
	public void UpdateDisplay() {
		var statusText = BuildStatusText();
		_statusLabel.Text = statusText;
		SetNeedsDraw();
	}
	
	private string BuildStatusText() {
		var viewIndicator = _state.CurrentView switch {
			ViewMode.Map => "MAP",
			ViewMode.Compress => "COMPRESS",
			_ => "UNKNOWN"
		};
		
		var modeIndicator = _state.CompactMode ? "COMPACT" : "EXPANDED";
		var selectionInfo = GetSelectionInfo();
		var shortcuts = GetShortcuts();
		
		return $"{viewIndicator} | {modeIndicator} | {selectionInfo}    {shortcuts}";
	}
	
	private string GetSelectionInfo() {
		if (_state.SelectedNode?.Symbol != null) {
			var symbol = _state.SelectedNode.Symbol;
			return $"{symbol.Name} ({symbol.Kind})";
		} else if (_state.SelectedNode != null) {
			return $"📁 {Path.GetFileName(_state.SelectedNode.Name)}";
		} else {
			return "No selection";
		}
	}
	
	private string GetShortcuts() {
		return _state.CurrentView switch {
			ViewMode.Map => "ENTER=Select prompt    TAB/V=Toggle view    R=Refresh    ESC/BKSP=Exit",
			ViewMode.Compress => "Processing compression...    ESC/BKSP=Cancel",
			_ => "ESC/BKSP=Exit"
		};
	}
}