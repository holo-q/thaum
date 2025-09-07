using System.Collections.ObjectModel;
using Terminal.Gui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Thaum.Core.Models;

namespace Thaum.TUI.Views;

/// <summary>
/// Minimal centered popup dialog for selecting compression prompts where available prompts
/// are loaded from the prompts directory with compress_function_v5 at the top as default
/// where the dialog maintains focus state and returns selected prompt name to caller
/// </summary>
public class PromptSelectorDialog {
	private readonly CodeSymbol _targetSymbol;
	private          string?    _selectedPrompt;

	public PromptSelectorDialog(CodeSymbol targetSymbol) {
		_targetSymbol = targetSymbol;
	}

	public string? ShowDialog() {
		var availablePrompts = GetAvailablePrompts();
		if (!availablePrompts.Any()) {
			MessageBox.ErrorQuery("No Prompts", "No prompt files found in prompts directory", "OK");
			return null;
		}

		// Create centered dialog
		var dialog = new Dialog() {
			Title = $"Select Prompt for {_targetSymbol.Name}",
			Width = 60,
			Height = 20,
			Modal = true
		};

		// Instructions
		var instructions = new Label {
			X      = 1,
			Y      = 1,
			Width  = Dim.Fill(1),
			Height = 2,
			Text   = $"Choose a prompt to apply to {_targetSymbol.Kind.ToString().ToLower()} '{_targetSymbol.Name}':\n"
		};

		// Prompt list
		var listView = new ListView {
			X             = 1,
			Y             = 3,
			Width         = Dim.Fill(1),
			Height        = Dim.Fill(3),
			AllowsMarking = false,
			CanFocus      = true
		};

		listView.SetSource(new ObservableCollection<string>(availablePrompts));
		listView.SelectedItem = 0; // Default to first item (compress_function_v5)

		// Buttons
		var selectButton = new Button() {
			Text      = "Select",
			X         = Pos.Center() - 10,
			Y         = Pos.Bottom(dialog) - 3,
			IsDefault = true
		};

		var cancelButton = new Button() {
			Text = "Cancel",
			X    = Pos.Center() + 2,
			Y    = Pos.Bottom(dialog) - 3
		};

		// Event handlers
		selectButton.Accepting += (sender, e) => {
			if (listView.SelectedItem >= 0 && listView.SelectedItem < availablePrompts.Count) {
				_selectedPrompt = ExtractPromptName(availablePrompts[listView.SelectedItem]);
			}
			dialog.RequestStop();
		};

		cancelButton.Accepting += (sender, e) => {
			_selectedPrompt = null;
			dialog.RequestStop();
		};

		// Handle Enter key on list
		listView.OpenSelectedItem += (sender, e) => selectButton.InvokeCommand(Command.Accept);

		// Handle Escape key - will be handled by default dialog behavior

		dialog.Add(instructions, listView, selectButton, cancelButton);

		// Show dialog
		Application.Run(dialog);

		return _selectedPrompt;
	}

	private List<string> GetAvailablePrompts() {
		var prompts    = new List<string>();
		var promptsDir = GLB.PromptsDir;

		if (!Directory.Exists(promptsDir)) {
			return prompts;
		}

		// Get all .txt and .md files from prompts directory
		var promptFiles = Directory.GetFiles(promptsDir, "*.txt")
			.Concat(Directory.GetFiles(promptsDir, "*.md"))
			.Select(Path.GetFileName)
			.Where(f => !string.IsNullOrEmpty(f))
			.Cast<string>()
			.ToList();

		// Sort with compress_function_v5 at the top
		var prioritizedPrompts = promptFiles
			.OrderBy(f => f.StartsWith("compress_function_v5") ? 0 : 1)
			.ThenBy(f => f.StartsWith("compress") ? 0 : 1)
			.ThenBy(f => f)
			.ToList();

		// Format for display with descriptions
		foreach (var prompt in prioritizedPrompts) {
			var description = GetPromptDescription(prompt);
			var displayName = $"{Path.GetFileNameWithoutExtension(prompt)} - {description}";
			prompts.Add(displayName);
		}

		return prompts;
	}

	private static string ExtractPromptName(string displayName) {
		// Extract the prompt name from "promptname - description" format
		var dashIndex = displayName.IndexOf(" - ", StringComparison.Ordinal);
		return dashIndex > 0 ? displayName[..dashIndex] : displayName;
	}

	private static string GetPromptDescription(string filename) {
		return Path.GetFileNameWithoutExtension(filename) switch {
			var name when name.StartsWith("compress_function") => "Function compression",
			var name when name.StartsWith("compress_class")    => "Class compression",
			var name when name.StartsWith("compress_key")      => "Key extraction",
			var name when name.StartsWith("optimize")          => "Code optimization",
			var name when name.StartsWith("golf")              => "Code golf",
			var name when name.StartsWith("fusion")            => "Fusion analysis",
			var name when name.StartsWith("grow")              => "Growth analysis",
			var name when name.StartsWith("infuse")            => "Semantic infusion",
			_                                                  => "Custom prompt"
		};
	}
}