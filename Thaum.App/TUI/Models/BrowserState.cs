using Thaum.CLI.Models;
using Thaum.Core.Models;

namespace Thaum.TUI.Models;

/// <summary>
/// Maintains state for the interactive symbol browser where selection and scroll position
/// are preserved across interactions and view mode changes where the state enables
/// seamless return to previous context after popup dialogs or mode switches
/// </summary>
public class BrowserState {
	public string ProjectPath { get; set; } = "";
	public string Language { get; set; } = "";
	public CodeMap CodeMap { get; set; } = CodeMap.Create();
	public List<TreeNode> DisplayNodes { get; set; } = [];
	
	// Navigation state
	public int SelectedIndex { get; set; } = 0;
	public int ScrollOffset { get; set; } = 0;
	
	// View configuration
	public bool CompactMode { get; set; } = true;
	
	// Current context
	public ViewMode CurrentView { get; set; } = ViewMode.Map;
	public TreeNode? SelectedNode => SelectedIndex >= 0 && SelectedIndex < DisplayNodes.Count 
		? DisplayNodes[SelectedIndex] 
		: null;
	
	/// <summary>
	/// Validates and adjusts selection index to ensure it remains within bounds
	/// after display list changes or filtering operations
	/// </summary>
	public void ValidateSelection() {
		if (DisplayNodes.Count == 0) {
			SelectedIndex = 0;
			ScrollOffset = 0;
			return;
		}
		
		SelectedIndex = Math.Max(0, Math.Min(SelectedIndex, DisplayNodes.Count - 1));
		ScrollOffset = Math.Max(0, ScrollOffset);
	}
}

public enum ViewMode {
	Map,
	Compress
}