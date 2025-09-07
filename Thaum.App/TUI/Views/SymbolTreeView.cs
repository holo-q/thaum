using System.Collections.ObjectModel;
using Terminal.Gui;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using TreeNode = Thaum.CLI.Models.TreeNode; // Resolve naming conflict
using Thaum.CLI.Models;
using Thaum.TUI.Models;
using Thaum.Core.Models;

namespace Thaum.TUI.Views;

/// <summary>
/// Enhanced scrollable symbol tree view supporting both compact and expanded display modes
/// where compact mode groups symbols by type on single lines while expanded mode shows
/// one symbol per line with full details and auto-scrolling based on current selection
/// </summary>
public class SymbolTreeView : View {
	private readonly BrowserState _state;
	private readonly ListView _listView;
	
	private List<string> _displayLines = [];
	private List<int> _nodeIndexMap = []; // Maps display line to node index
	
	public SymbolTreeView(BrowserState state) {
		_state = state;
		
		// Force terminal colors for proper transparency
		ColorScheme = Colors.ColorSchemes["Base"];
		
		_listView = new ListView {
			X = 0,
			Y = 0,
			Width = Dim.Fill(),
			Height = Dim.Fill(),
			AllowsMarking = false,
			CanFocus = true,
			ColorScheme = Colors.ColorSchemes["Base"]
		};
		
		Add(_listView);
		
		_listView.OpenSelectedItem += OnSelectionChanged;
	}
	
	public void UpdateNodes(List<TreeNode> nodes) {
		RebuildDisplayLines();
		RefreshListView();
	}
	
	public void SetSelection(int nodeIndex) {
		// Find the display line that corresponds to this node
		for (int i = 0; i < _nodeIndexMap.Count; i++) {
			if (_nodeIndexMap[i] == nodeIndex) {
				_listView.SelectedItem = i;
				EnsureVisible(i);
				break;
			}
		}
	}
	
	public void EnsureSelectionVisible() {
		if (_listView.SelectedItem >= 0) {
			EnsureVisible(_listView.SelectedItem);
		}
	}
	
	public void SetCompactMode(bool compact) {
		_state.CompactMode = compact;
		RebuildDisplayLines();
		RefreshListView();
		
		// Maintain selection after mode change
		SetSelection(_state.SelectedIndex);
	}
	
	private void RebuildDisplayLines() {
		_displayLines.Clear();
		_nodeIndexMap.Clear();
		
		if (_state.CompactMode) {
			BuildCompactDisplay();
		} else {
			BuildExpandedDisplay();
		}
	}
	
	private void BuildCompactDisplay() {
		// Group symbols similar to the ls command output
		for (int nodeIndex = 0; nodeIndex < _state.DisplayNodes.Count; nodeIndex++) {
			var fileNode = _state.DisplayNodes[nodeIndex];
			
			// Add file header - make it selectable
			_displayLines.Add($"└── [DIR] {fileNode.Name}");
			_nodeIndexMap.Add(nodeIndex); // File headers are now selectable
			
			if (fileNode.Children.Any()) {
				// Group children by symbol kind
				var groupedChildren = fileNode.Children.GroupBy(c => c.Kind).ToList();
				
				foreach (var group in groupedChildren) {
					var symbols = group.ToList();
					var kindName = GetKindDisplayName(group.Key);
					var icon = IconProvider.GetSymbolKindIcon(group.Key);
					
					// Create compact line with multiple symbols
					var symbolNames = symbols.Select(s => s.Name).Take(5); // Limit to avoid overflow
					var remaining = symbols.Count > 5 ? $" +{symbols.Count - 5}" : "";
					var line = $"    ├── {icon} {kindName}: {string.Join(" ", symbolNames)}{remaining}";
					
					_displayLines.Add(line);
					
					// Map to the same node index for group selection
					_nodeIndexMap.Add(nodeIndex);
				}
			}
		}
	}
	
	private void BuildExpandedDisplay() {
		// Show each symbol on its own line
		for (int nodeIndex = 0; nodeIndex < _state.DisplayNodes.Count; nodeIndex++) {
			var node = _state.DisplayNodes[nodeIndex];
			
			// File header
			_displayLines.Add($"└── [DIR] {node.Name}");
			_nodeIndexMap.Add(nodeIndex);
			
			// Individual symbols
			foreach (var child in node.Children.OrderBy(c => c.Name)) {
				var icon = IconProvider.GetSymbolKindIcon(child.Kind);
				var line = $"    ├── {icon} {child.Name}";
				
				_displayLines.Add(line);
				_nodeIndexMap.Add(nodeIndex); // Map back to parent file node
			}
		}
	}
	
	private void RefreshListView() {
		_listView.SetSource(new ObservableCollection<string>(_displayLines));
		// v2 API: Content size handled automatically by ListView
	}
	
	private void EnsureVisible(int displayIndex) {
		// v2 API: ListView handles scrolling automatically
		_listView.EnsureSelectedItemVisible();
	}
	
	private void OnSelectionChanged(object? sender, ListViewItemEventArgs e) {
		var selectedLine = _listView.SelectedItem;
		if (selectedLine >= 0 && selectedLine < _nodeIndexMap.Count) {
			var nodeIndex = _nodeIndexMap[selectedLine];
			if (nodeIndex >= 0 && nodeIndex < _state.DisplayNodes.Count) {
				_state.SelectedIndex = nodeIndex;
			}
		}
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