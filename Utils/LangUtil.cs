namespace Thaum.Utils;

/// <summary>
/// Language detection utilities where file extensions reveal intent where directory scanning
/// determines primary language where extension mapping creates language awareness where the
/// utility provides ambient language detection without ceremony eliminating redundant detection
/// logic across crawlers where centralized detection ensures consistency
/// </summary>
public static class LangUtil {
	/// <summary>
	/// Detects language from file extension where .cs maps to c-sharp where .py maps to python
	/// where the mapping follows common conventions where fallback ensures reasonable default
	/// </summary>
	public static string DetectLanguageFromFile(string filepath) {
		string extension = Path.GetExtension(filepath).ToLowerInvariant();

		return extension switch {
			".py"                     => "python",
			".cs"                     => "c-sharp",
			".js" or ".jsx"           => "javascript",
			".ts" or ".tsx"           => "typescript",
			".rs"                     => "rust",
			".go"                     => "go",
			".java"                   => "java",
			".cpp" or ".cc" or ".cxx" => "cpp",
			".c" or ".h"              => "c",
			".rb"                     => "ruby",
			".php"                    => "php",
			".swift"                  => "swift",
			".kt" or ".kts"           => "kotlin",
			".scala"                  => "scala",
			".r"                      => "r",
			".jl"                     => "julia",
			".lua"                    => "lua",
			".dart"                   => "dart",
			".hs"                     => "haskell",
			".ml" or ".mli"           => "ocaml",
			".ex" or ".exs"           => "elixir",
			".clj" or ".cljs"         => "clojure",
			".nim"                    => "nim",
			".zig"                    => "zig",
			_                         => "c-sharp" // Default fallback
		};
	}

	/// <summary>
	/// Detects primary language in directory where file counts determine dominance where
	/// C# takes precedence in mixed codebases where the detection creates project awareness
	/// </summary>
	public static string DetectLanguageFromDirectory(string dirpath) {
		// Count files by extension to determine primary language
		var extensionCounts = Directory.GetFiles(dirpath, "*.*", SearchOption.AllDirectories)
			.Select(f => Path.GetExtension(f).ToLowerInvariant())
			.Where(ext => !string.IsNullOrEmpty(ext))
			.GroupBy(ext => ext)
			.ToDictionary(g => g.Key, g => g.Count());

		// Determine language based on most common source file extension
		// Prioritize by ecosystem importance and project likelihood
		if (extensionCounts.ContainsKey(".cs")) return "c-sharp";
		if (extensionCounts.ContainsKey(".py")) return "python";
		if (extensionCounts.ContainsKey(".ts") || extensionCounts.ContainsKey(".tsx")) return "typescript";
		if (extensionCounts.ContainsKey(".js") || extensionCounts.ContainsKey(".jsx")) return "javascript";
		if (extensionCounts.ContainsKey(".rs")) return "rust";
		if (extensionCounts.ContainsKey(".go")) return "go";
		if (extensionCounts.ContainsKey(".java")) return "java";
		if (extensionCounts.ContainsKey(".cpp") || extensionCounts.ContainsKey(".cc")) return "cpp";
		if (extensionCounts.ContainsKey(".c")) return "c";
		if (extensionCounts.ContainsKey(".rb")) return "ruby";
		if (extensionCounts.ContainsKey(".php")) return "php";
		if (extensionCounts.ContainsKey(".swift")) return "swift";
		if (extensionCounts.ContainsKey(".kt")) return "kotlin";

		return "c-sharp"; // Default fallback
	}

	/// <summary>
	/// Checks if file matches language where extension determines match where the check
	/// enables filtered crawling where language-specific processing becomes possible
	/// </summary>
	public static bool IsSourceFileForLanguage(string filePath, string language) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();

		return language.ToLowerInvariant() switch {
			"python"     => extension == ".py",
			"c-sharp"    => extension == ".cs",
			"javascript" => extension is ".js" or ".jsx",
			"typescript" => extension is ".ts" or ".tsx",
			"rust"       => extension == ".rs",
			"go"         => extension == ".go",
			"java"       => extension == ".java",
			"cpp"        => extension is ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh",
			"c"          => extension is ".c" or ".h",
			"ruby"       => extension == ".rb",
			"php"        => extension == ".php",
			"swift"      => extension == ".swift",
			"kotlin"     => extension is ".kt" or ".kts",
			"scala"      => extension == ".scala",
			"r"          => extension == ".r",
			"julia"      => extension == ".jl",
			"lua"        => extension == ".lua",
			"dart"       => extension == ".dart",
			"haskell"    => extension == ".hs",
			"ocaml"      => extension is ".ml" or ".mli",
			"elixir"     => extension is ".ex" or ".exs",
			"clojure"    => extension is ".clj" or ".cljs",
			"nim"        => extension == ".nim",
			"zig"        => extension == ".zig",
			_            => false
		};
	}

	/// <summary>
	/// Gets common file extensions for language where patterns guide file discovery where
	/// glob patterns enable efficient filtering where language awareness improves crawling
	/// </summary>
	public static string[] GetExtensionsForLanguage(string language) {
		return language.ToLowerInvariant() switch {
			"python"     => [".py"],
			"c-sharp"    => [".cs"],
			"javascript" => [".js", ".jsx", ".mjs"],
			"typescript" => [".ts", ".tsx", ".mts"],
			"rust"       => [".rs"],
			"go"         => [".go"],
			"java"       => [".java"],
			"cpp"        => [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".h"],
			"c"          => [".c", ".h"],
			"ruby"       => [".rb"],
			"php"        => [".php"],
			"swift"      => [".swift"],
			"kotlin"     => [".kt", ".kts"],
			"scala"      => [".scala"],
			"r"          => [".r", ".R"],
			"julia"      => [".jl"],
			"lua"        => [".lua"],
			"dart"       => [".dart"],
			"haskell"    => [".hs", ".lhs"],
			"ocaml"      => [".ml", ".mli"],
			"elixir"     => [".ex", ".exs"],
			"clojure"    => [".clj", ".cljs", ".cljc"],
			"nim"        => [".nim"],
			"zig"        => [".zig"],
			_            => [".txt"] // Fallback to text files
		};
	}

	public static string DetectLanguage(string projectPath) {
		string[]             files      = Directory.GetFiles(projectPath, "*.*", SearchOption.TopDirectoryOnly);
		IEnumerable<string?> extensions = files.Select(Path.GetExtension).Where(ext => !string.IsNullOrEmpty(ext));

		Dictionary<string, int> counts = extensions
			.GroupBy(ext => ext.ToLowerInvariant())
			.ToDictionary(g => g.Key, g => g.Count());

		// Check for specific project files first
		if (File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
		    File.Exists(Path.Combine(projectPath, "requirements.txt")) ||
		    counts.GetValueOrDefault(".py", 0) > 0)
			return "python";

		if (File.Exists(Path.Combine(projectPath, "*.csproj")) ||
		    File.Exists(Path.Combine(projectPath, "*.sln")) ||
		    counts.GetValueOrDefault(".cs", 0) > 0)
			return "c-sharp";

		if (File.Exists(Path.Combine(projectPath, "package.json"))) {
			return counts.GetValueOrDefault(".ts", 0) > counts.GetValueOrDefault(".js", 0) ? "typescript" : "javascript";
		}

		if (File.Exists(Path.Combine(projectPath, "Cargo.toml")))
			return "rust";

		if (File.Exists(Path.Combine(projectPath, "go.mod")))
			return "go";

		// Fallback to most common extension
		KeyValuePair<string, int> mostCommon = counts.OrderByDescending(kv => kv.Value).FirstOrDefault();
		return mostCommon.Key switch {
			".py" => "python",
			".cs" => "c-sharp",
			".js" => "javascript",
			".ts" => "typescript",
			".rs" => "rust",
			".go" => "go",
			_     => "python" // Default fallback
		};
	}

	public static string DetectLanguageInternal(string projectPath, string language) {
		return language != "auto"
			? language
			: LangUtil.DetectLanguage(projectPath);
	}
}