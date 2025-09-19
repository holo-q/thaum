using System.Text.RegularExpressions;

namespace Thaum.Core.Utils;

/// <summary>
/// Handles project exclusions including .gitignore patterns and project-specific ignores
/// where common build artifacts/dependencies/temporary files are filtered to improve
/// performance and focus on source code files where exclusion patterns follow gitignore
/// syntax supporting glob patterns/negation/directory matching
/// </summary>
public static class ProjectExclusions {
	/// <summary>
	/// Project-specific default exclusions based on detected project type
	/// </summary>
	private static readonly Dictionary<string, string[]> DefaultExclusions = new() {
		["c-sharp"] = [
			"bin/", "obj/", "*.user", "*.suo", "*.cache",
			"packages/", ".vs/", ".vscode/", "external/",
			"node_modules/", "wwwroot/lib/", "TestResults/"
		],
		["javascript"] = [
			"node_modules/", "dist/", "build/", ".next/", ".nuxt/",
			"coverage/", ".nyc_output/", ".cache/", ".parcel-cache/"
		],
		["typescript"] = [
			"node_modules/", "dist/", "build/", ".next/", ".nuxt/",
			"coverage/", ".nyc_output/", ".cache/", ".parcel-cache/",
			"*.d.ts", "*.js.map"
		],
		["python"] = [
			"__pycache__/", "*.pyc", "*.pyo", "*.pyd", ".Python",
			"build/", "develop-eggs/", "dist/", "downloads/", "eggs/",
			".eggs/", "lib/", "lib64/", "parts/", "sdist/", "var/",
			"wheels/", ".env", ".venv/", "venv/", "ENV/", ".coverage"
		],
		["rust"] = [
			"target/", "Cargo.lock", "*.pdb", "*.exe", "*.dll"
		],
		["go"] = [
			"vendor/", "*.exe", "*.exe~", "*.dll", "*.so", "*.dylib",
			"*.test", "*.out", ".cache/", "go.work", "go.work.sum"
		],
		["java"] = [
			"target/", "build/", "*.class", "*.jar", "*.war", "*.ear",
			".gradle/", ".idea/", "*.iml", "*.ipr", "*.iws"
		]
	};

	/// <summary>
	/// Universal exclusions applied to all projects
	/// </summary>
	private static readonly string[] UniversalExclusions = [
		".git/", ".svn/", ".hg/", ".bzr/",
		"*.tmp", "*.temp", "*.log", "*.swp", "*.swo",
		".DS_Store", "Thumbs.db", "desktop.ini",
		"*.orig", "*.rej", "*~"
	];

	/// <summary>
	/// Checks if a file or directory should be excluded based on .gitignore and project patterns
	/// </summary>
	public static bool ShouldExclude(string filePath, string projectRoot, string? language = null) {
		string relativePath = Path.GetRelativePath(projectRoot, filePath);
		
		// Normalize path separators for consistent matching
		relativePath = relativePath.Replace('\\', '/');
		
		// Check universal exclusions
		if (IsExcludedByPatterns(relativePath, UniversalExclusions)) {
			return true;
		}
		
		// Check project-specific exclusions
		if (language != null && DefaultExclusions.TryGetValue(language, out string[]? patterns)) {
			if (IsExcludedByPatterns(relativePath, patterns)) {
				return true;
			}
		}
		
		// Check .gitignore patterns
		List<GitignorePattern> gitignorePatterns = LoadGitignorePatterns(projectRoot);
		if (IsExcludedByGitignorePatterns(relativePath, gitignorePatterns)) {
			return true;
		}
		
		return false;
	}

	/// <summary>
	/// Loads and parses .gitignore patterns from the project root
	/// </summary>
	private static List<GitignorePattern> LoadGitignorePatterns(string projectRoot) {
		List<GitignorePattern> patterns      = new List<GitignorePattern>();
		string gitignorePath = Path.Combine(projectRoot, ".gitignore");
		
		if (!File.Exists(gitignorePath)) {
			return patterns;
		}
		
		foreach (string line in File.ReadAllLines(gitignorePath)) {
			string trimmed = line.Trim();
			
			// Skip empty lines and comments
			if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) {
				continue;
			}
			
			// Handle negation patterns
			bool isNegation = trimmed.StartsWith('!');
			if (isNegation) {
				trimmed = trimmed[1..];
			}
			
			// Convert gitignore pattern to regex
			string regexPattern = ConvertGitignoreToRegex(trimmed);
			
			patterns.Add(new GitignorePattern(regexPattern, isNegation, trimmed.EndsWith('/')));
		}
		
		return patterns;
	}

	/// <summary>
	/// Checks if a path is excluded by simple glob patterns
	/// </summary>
	private static bool IsExcludedByPatterns(string relativePath, string[] patterns) {
		foreach (string pattern in patterns) {
			if (MatchesPattern(relativePath, pattern)) {
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Checks if a path is excluded by gitignore patterns with proper negation support
	/// </summary>
	private static bool IsExcludedByGitignorePatterns(string relativePath, List<GitignorePattern> patterns) {
		bool isExcluded = false;
		
		foreach (GitignorePattern pattern in patterns) {
			if (Regex.IsMatch(relativePath, pattern.Regex)) {
				if (pattern.IsNegation) {
					isExcluded = false; // Negation patterns un-exclude
				} else {
					isExcluded = true;
				}
			}
		}
		
		return isExcluded;
	}

	/// <summary>
	/// Simple glob pattern matching for basic exclusions
	/// </summary>
	private static bool MatchesPattern(string path, string pattern) {
		// Handle directory patterns ending with /
		if (pattern.EndsWith('/')) {
			string dirPattern = pattern[..^1];
			return path.StartsWith(dirPattern + "/") || 
			       path.Split('/').Any(segment => segment == dirPattern);
		}
		
		// Handle file extension patterns like *.tmp
		if (pattern.StartsWith("*.")) {
			string extension = pattern[1..];
			return path.EndsWith(extension);
		}
		
		// Handle exact matches
		if (path == pattern) {
			return true;
		}
		
		// Handle directory/file name patterns
		return path.Split('/').Any(segment => segment == pattern);
	}

	/// <summary>
	/// Converts gitignore glob patterns to regex patterns
	/// </summary>
	private static string ConvertGitignoreToRegex(string gitignorePattern) {
		string pattern = gitignorePattern;
		
		// Escape special regex characters except * and ?
		pattern = Regex.Escape(pattern);
		
		// Unescape * and ? for glob matching
		pattern = pattern.Replace(@"\*", ".*").Replace(@"\?", ".");
		
		// Handle directory patterns
		if (pattern.EndsWith(@"\/")) {
			pattern = pattern[..^2] + @"($|\/)";
		}
		
		// Anchor pattern appropriately
		if (!pattern.StartsWith(".*")) {
			pattern = "(^|/)" + pattern;
		}
		
		return pattern;
	}

	/// <summary>
	/// Represents a parsed gitignore pattern
	/// </summary>
	private record GitignorePattern(string Regex, bool IsNegation, bool IsDirectory);
}