using Thaum.Core.Crawling;

namespace Thaum.Core.Utils;

/// <summary>
/// Centralized icon provider that leverages Nerd Fonts for enhanced TUI visualization
/// where modern terminals get rich iconography while providing graceful fallbacks
/// for environments without Nerd Font support enabling consistent visual experience
/// </summary>
public static class IconProvider {
	/// <summary>
	/// Enables Nerd Font icons by default where modern terminals support rich iconography
	/// while allowing configuration override for specific deployment environments
	/// </summary>
	public static bool EnableNerdFonts { get; set; } = true;

	/// <summary>
	/// Gets semantic icon for symbol kinds with Nerd Font enhancement where each symbol type
	/// receives distinctive visual representation creating instant recognition patterns
	/// that accelerate code navigation and comprehension
	/// </summary>
	public static string GetSymbolKindIcon(SymbolKind kind) {
		if (!EnableNerdFonts) {
			return GetFallbackSymbolIcon(kind);
		}

		return kind switch {
			SymbolKind.Function    => "", // nf-cod-symbol_method
			SymbolKind.Method      => "", // nf-cod-symbol_method  
			SymbolKind.Class       => "", // nf-cod-symbol_class
			SymbolKind.Interface   => "", // nf-cod-symbol_interface
			SymbolKind.Module      => "", // nf-cod-symbol_namespace
			SymbolKind.Namespace   => "", // nf-cod-symbol_namespace
			SymbolKind.Property    => "", // nf-cod-symbol_property
			SymbolKind.Field       => "", // nf-cod-symbol_field
			SymbolKind.Variable    => "", // nf-cod-symbol_variable
			SymbolKind.Parameter   => "", // nf-cod-symbol_parameter
			SymbolKind.Enum        => "", // nf-cod-symbol_enum
			SymbolKind.EnumMember  => "", // nf-cod-symbol_enum_member
			SymbolKind.Constructor => "", // nf-cod-tools
			_                      => ""  // nf-cod-symbol_misc
		};
	}

	/// <summary>
	/// Gets semantic icon based on method naming patterns where async operations get lightning
	/// where data readers get books where writers get pencils creating visual language
	/// that maps developer mental models to immediate visual recognition
	/// </summary>
	public static string GetMethodPatternIcon(string methodName) {
		if (!EnableNerdFonts) {
			return GetFallbackMethodIcon(methodName);
		}

		// Pattern matching for common method naming conventions
		if (methodName.EndsWith("Async")) return "";                                      // nf-mdi-flash
		if (methodName.StartsWith("Get") || methodName.StartsWith("Read")) return "";     // nf-cod-book
		if (methodName.StartsWith("Set") || methodName.StartsWith("Update")) return "";   // nf-cod-edit
		if (methodName.StartsWith("Handle") || methodName.Contains("Process")) return ""; // nf-cod-settings_gear
		if (methodName.StartsWith("Build") || methodName.StartsWith("Create")) return ""; // nf-cod-tools
		if (methodName.StartsWith("Load") || methodName.Contains("Import")) return "";    // nf-cod-cloud_download
		if (methodName.StartsWith("Save") || methodName.StartsWith("Write")) return "";   // nf-cod-save
		if (methodName.Contains("Dispose") || methodName.Contains("Clean")) return "";    // nf-cod-trash
		if (methodName.Contains("Validate") || methodName.Contains("Check")) return "";   // nf-cod-check
		if (methodName.Contains("Parse") || methodName.Contains("Convert")) return "";    // nf-cod-symbol_operator
		if (methodName.Contains("Send") || methodName.Contains("Publish")) return "";     // nf-cod-export
		if (methodName.Contains("Start") || methodName.Contains("Begin")) return "";      // nf-cod-play
		if (methodName.Contains("Stop") || methodName.Contains("End")) return "";         // nf-cod-debug_stop
		if (methodName.Contains("Test") || methodName.Contains("Assert")) return "";      // nf-cod-beaker

		return ""; // nf-cod-symbol_method (default method icon)
	}

	/// <summary>
	/// Gets status icon for symbol processing state where completed symbols show checkmarks
	/// where key-extracted symbols get key indicators creating immediate visual feedback
	/// about processing progress and symbol state
	/// </summary>
	public static string GetSymbolStatusIcon(CodeSymbol symbol) {
		if (!EnableNerdFonts) {
			return GetFallbackStatusIcon(symbol);
		}

		if (symbol.IsSummarized) {
			return symbol.HasExtractedKey ? "" : ""; // nf-cod-key or nf-cod-check
		}
		return ""; // nf-cod-circle_large_outline (pending)
	}

	/// <summary>
	/// Gets file type icon based on extension patterns providing visual file type recognition
	/// that leverages established conventions from popular file managers and editors
	/// </summary>
	public static string GetFileTypeIcon(string filename) {
		if (!EnableNerdFonts) {
			return ""; // Simple folder icon fallback
		}

		string extension = Path.GetExtension(filename).ToLowerInvariant();
		return extension switch {
			".cs"   => "", // nf-mdi-language_csharp
			".js"   => "", // nf-dev-javascript  
			".ts"   => "", // nf-seti-typescript
			".py"   => "", // nf-dev-python
			".java" => "", // nf-fae-java
			".cpp"  => "", // nf-mdi-language_cpp
			".c"    => "", // nf-mdi-language_c
			".go"   => "", // nf-dev-go
			".rs"   => "", // nf-dev-rust
			".php"  => "", // nf-dev-php
			".rb"   => "", // nf-oct-ruby
			".json" => "", // nf-seti-json
			".xml"  => "", // nf-mdi-file_xml
			".md"   => "", // nf-dev-markdown
			".txt"  => "", // nf-mdi-file_document
			".log"  => "", // nf-fa-list
			_       => ""  // nf-cod-file (default file)
		};
	}

	/// <summary>
	/// ASCII fallback icons for terminals without Nerd Font support maintaining basic
	/// visual distinction while ensuring universal compatibility across all environments
	/// </summary>
	private static string GetFallbackSymbolIcon(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function    => "ƒ",
			SymbolKind.Method      => "ƒ",
			SymbolKind.Class       => "C",
			SymbolKind.Interface   => "I",
			SymbolKind.Module      => "M",
			SymbolKind.Namespace   => "N",
			SymbolKind.Property    => "P",
			SymbolKind.Field       => "F",
			SymbolKind.Variable    => "V",
			SymbolKind.Parameter   => "p",
			SymbolKind.Enum        => "E",
			SymbolKind.EnumMember  => "e",
			SymbolKind.Constructor => "@",
			_                      => "?"
		};
	}

	/// <summary>
	/// Emoji fallback for method patterns providing intermediate visual enhancement
	/// for terminals that support Unicode but lack Nerd Font installation
	/// </summary>
	private static string GetFallbackMethodIcon(string methodName) {
		if (methodName.EndsWith("Async")) return "⚡";
		if (methodName.StartsWith("Get") || methodName.StartsWith("Read")) return "📖";
		if (methodName.StartsWith("Set") || methodName.StartsWith("Update")) return "✏️";
		if (methodName.StartsWith("Handle") || methodName.Contains("Process")) return "🎛️";
		if (methodName.StartsWith("Build") || methodName.StartsWith("Create")) return "🔨";
		if (methodName.StartsWith("Load") || methodName.Contains("Import")) return "📥";
		if (methodName.StartsWith("Save") || methodName.StartsWith("Write")) return "💾";
		if (methodName.Contains("Dispose") || methodName.Contains("Clean")) return "🗑️";

		return "🔧";
	}

	/// <summary>
	/// ASCII status fallback maintaining basic status indication across all terminal types
	/// </summary>
	private static string GetFallbackStatusIcon(CodeSymbol symbol) {
		if (symbol.IsSummarized) {
			return symbol.HasExtractedKey ? "[✓K]" : "[✓]";
		}
		return "[ ]";
	}
}