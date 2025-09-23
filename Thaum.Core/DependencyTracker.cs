using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Thaum.Meta;

namespace Thaum.Core;

[LoggingIntrinsics]
public abstract partial class DependencyTracker {
	private readonly ILogger<DependencyTracker>                           _logger;
	private readonly ConcurrentDictionary<string, ProjectDependencyGraph> _projectGraphs = new();

	public DependencyTracker(ILogger<DependencyTracker> logger) {
		_logger = logger;
	}

	public async Task BuildDependencyGraphAsync(string projectPath, string language) {
		try {
			info("Building dependency graph for {ProjectPath} ({Language})", projectPath, language);

			ProjectDependencyGraph graph = new ProjectDependencyGraph();
			List<string> sourceFiles = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
				.Where(f => IsSourceFileForLanguage(f, language))
				.ToList();

			IDependencyAnalyzer analyzer = CreateAnalyzer(language);

			foreach (string file in sourceFiles) {
				List<string> dependencies = await analyzer.GetDependenciesAsync(file);
				graph.SetDependencies(file, dependencies);
			}

			_projectGraphs[projectPath] = graph;
			info("Built dependency graph with {FileCount} files and {DependencyCount} dependencies",
				sourceFiles.Count, graph.GetTotalDependencyCount());
		} catch (Exception ex) {
			err(ex, "Error building dependency graph for {ProjectPath}", projectPath);
			throw;
		}
	}

	public async Task UpdateDependencyAsync(string filePath, List<string> dependencies) {
		try {
			string? projectPath = FindProjectPath(filePath);
			if (projectPath != null && _projectGraphs.TryGetValue(projectPath, out ProjectDependencyGraph? graph)) {
				graph.SetDependencies(filePath, dependencies);
				trace("Updated dependencies for {FilePath}: {DependencyCount} dependencies",
					filePath, dependencies.Count);
			}

			await Task.CompletedTask;
		} catch (Exception ex) {
			err(ex, "Error updating dependency for {FilePath}", filePath);
			throw;
		}
	}

	public async Task<List<string>> GetDependentsAsync(string filePath) {
		try {
			string? projectPath = FindProjectPath(filePath);
			if (projectPath == null || !_projectGraphs.TryGetValue(projectPath, out ProjectDependencyGraph? graph)) {
				return [];
			}

			List<string> dependents = graph.GetDependents(filePath);
			await Task.CompletedTask;

			return dependents;
		} catch (Exception ex) {
			err(ex, "Error getting dependents for {FilePath}", filePath);
			return [];
		}
	}

	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		try {
			string? projectPath = FindProjectPath(filePath);
			if (projectPath == null || !_projectGraphs.TryGetValue(projectPath, out ProjectDependencyGraph? graph)) {
				return [];
			}

			List<string> dependencies = graph.GetDependencies(filePath);
			await Task.CompletedTask;

			return dependencies;
		} catch (Exception ex) {
			err(ex, "Error getting dependencies for {FilePath}", filePath);
			return [];
		}
	}

	public async Task RemoveFileAsync(string filePath) {
		try {
			string? projectPath = FindProjectPath(filePath);
			if (projectPath != null && _projectGraphs.TryGetValue(projectPath, out ProjectDependencyGraph? graph)) {
				graph.RemoveFile(filePath);
				trace("Removed {FilePath} from dependency graph", filePath);
			}

			await Task.CompletedTask;
		} catch (Exception ex) {
			err(ex, "Error removing file from dependency graph: {FilePath}", filePath);
			throw;
		}
	}

	public void ClearGraph(string projectPath) {
		_projectGraphs.TryRemove(projectPath, out _);
		info("Cleared dependency graph for {ProjectPath}", projectPath);
	}

	private string? FindProjectPath(string filePath) {
		return _projectGraphs.Keys
			.Where(projectPath => filePath.StartsWith(projectPath))
			.OrderByDescending(p => p.Length) // Get most specific match
			.FirstOrDefault();
	}

	private static bool IsSourceFileForLanguage(string filePath, string language) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();

		return language.ToLowerInvariant() switch {
			"python"     => extension == ".py",
			"c-sharp"    => extension == ".cs",
			"javascript" => extension is ".js" or ".jsx",
			"typescript" => extension is ".ts" or ".tsx",
			"rust"       => extension == ".rs",
			"go"         => extension == ".go",
			"java"       => extension == ".java",
			"cpp"        => extension is ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp",
			"c"          => extension is ".c" or ".h",
			_            => false
		};
	}

	private IDependencyAnalyzer CreateAnalyzer(string language) {
		return language.ToLowerInvariant() switch {
			"python"     => new PythonDependencyAnalyzer(),
			"c-sharp"    => new CSharpDependencyAnalyzer(),
			"javascript" => new JavaScriptDependencyAnalyzer(),
			"typescript" => new TypeScriptDependencyAnalyzer(),
			"rust"       => new RustDependencyAnalyzer(),
			"go"         => new GoDependencyAnalyzer(),
			_            => new GenericDependencyAnalyzer()
		};
	}
}

internal class ProjectDependencyGraph {
	private readonly ConcurrentDictionary<string, HashSet<string>> _dependencies = new();
	private readonly ConcurrentDictionary<string, HashSet<string>> _dependents   = new();

	public void SetDependencies(string filePath, List<string> dependencies) {
		// Clear existing dependencies
		if (_dependencies.TryGetValue(filePath, out HashSet<string>? oldDeps)) {
			foreach (string dep in oldDeps) {
				if (_dependents.TryGetValue(dep, out HashSet<string>? depSet)) {
					depSet.Remove(filePath);
				}
			}
		}

		// Set new dependencies
		HashSet<string> newDeps = dependencies.ToHashSet();
		_dependencies[filePath] = newDeps;

		// Update reverse mapping (dependents)
		foreach (string dep in dependencies) {
			_dependents.AddOrUpdate(dep,
				[filePath],
				(_, existing) => {
					existing.Add(filePath);
					return existing;
				});
		}
	}

	public List<string> GetDependencies(string filePath) {
		return _dependencies.TryGetValue(filePath, out HashSet<string>? deps)
			? deps.ToList()
			: [];
	}

	public List<string> GetDependents(string filePath) {
		return _dependents.TryGetValue(filePath, out HashSet<string>? deps)
			? deps.ToList()
			: [];
	}

	public void RemoveFile(string filePath) {
		// Remove from dependencies
		if (_dependencies.TryRemove(filePath, out HashSet<string>? deps)) {
			// Remove from dependents of files this file depended on
			foreach (string dep in deps) {
				if (_dependents.TryGetValue(dep, out HashSet<string>? depSet)) {
					depSet.Remove(filePath);
				}
			}
		}

		// Remove from dependents
		if (_dependents.TryRemove(filePath, out HashSet<string>? dependents)) {
			// Remove this file from the dependencies of files that depended on it
			foreach (string dependent in dependents) {
				if (_dependencies.TryGetValue(dependent, out HashSet<string>? depSet)) {
					depSet.Remove(filePath);
				}
			}
		}
	}

	public int GetTotalDependencyCount() {
		return _dependencies.Values.Sum(deps => deps.Count);
	}
}

internal interface IDependencyAnalyzer {
	Task<List<string>> GetDependenciesAsync(string filePath);
}

internal class PythonDependencyAnalyzer : IDependencyAnalyzer {
	private static readonly Regex ImportRegex = new(@"^\s*(?:from\s+(\S+)\s+)?import\s+(.+)$", RegexOptions.Multiline);

	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		HashSet<string> dependencies = [];

		try {
			string          content = await File.ReadAllTextAsync(filePath);
			MatchCollection matches = ImportRegex.Matches(content);

			foreach (Match match in matches) {
				string fromModule    = match.Groups[1].Value;
				string importedItems = match.Groups[2].Value;

				if (!string.IsNullOrEmpty(fromModule) && !fromModule.StartsWith('.')) {
					dependencies.Add(fromModule);
				}

				// Handle direct imports
				IEnumerable<string> items = importedItems.Split(',').Select(i => i.Trim().Split(' ')[0]);
				foreach (string item in items.Where(i => !string.IsNullOrEmpty(i))) {
					dependencies.Add(item);
				}
			}
		} catch (Exception) {
			// Ignore file read errors
		}

		return dependencies.ToList();
	}
}

internal class CSharpDependencyAnalyzer : IDependencyAnalyzer {
	private static readonly Regex UsingRegex = new(@"^\s*using\s+([^;]+);", RegexOptions.Multiline);

	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		HashSet<string> dependencies = [];

		try {
			string          content = await File.ReadAllTextAsync(filePath);
			MatchCollection matches = UsingRegex.Matches(content);

			foreach (Match match in matches) {
				string usingStatement = match.Groups[1].Value.Trim();
				if (!usingStatement.Contains('=') && !usingStatement.StartsWith("static")) {
					dependencies.Add(usingStatement);
				}
			}
		} catch (Exception) {
			// Ignore file read errors
		}

		return dependencies.ToList();
	}
}

internal class JavaScriptDependencyAnalyzer : IDependencyAnalyzer {
	private static readonly Regex ImportRegex = new(@"^\s*(?:import\s+.+\s+from\s+['""]([^'""]+)['""]|require\s*\(\s*['""]([^'""]+)['""]\s*\))", RegexOptions.Multiline);

	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		HashSet<string> dependencies = [];

		try {
			string          content = await File.ReadAllTextAsync(filePath);
			MatchCollection matches = ImportRegex.Matches(content);

			foreach (Match match in matches) {
				string moduleName = match.Groups[1].Value.IsNullOrEmpty() ? match.Groups[2].Value : match.Groups[1].Value;
				if (!string.IsNullOrEmpty(moduleName)) {
					dependencies.Add(moduleName);
				}
			}
		} catch (Exception) {
			// Ignore file read errors
		}

		return dependencies.ToList();
	}
}

internal class TypeScriptDependencyAnalyzer : JavaScriptDependencyAnalyzer {
	// TypeScript uses same import syntax as JavaScript
}

internal class RustDependencyAnalyzer : IDependencyAnalyzer {
	private static readonly Regex UseRegex = new(@"^\s*use\s+([^;]+);", RegexOptions.Multiline);

	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		HashSet<string> dependencies = [];

		try {
			string          content = await File.ReadAllTextAsync(filePath);
			MatchCollection matches = UseRegex.Matches(content);

			foreach (Match match in matches) {
				string useStatement = match.Groups[1].Value.Trim();
				string rootModule   = useStatement.Split(["::", "."], StringSplitOptions.RemoveEmptyEntries)[0];
				dependencies.Add(rootModule);
			}
		} catch (Exception) {
			// Ignore file read errors
		}

		return dependencies.ToList();
	}
}

internal class GoDependencyAnalyzer : IDependencyAnalyzer {
	private static readonly Regex ImportRegex = new(@"^\s*import\s+(?:\(\s*|\s*[""']([^""']+)[""'])", RegexOptions.Multiline);

	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		HashSet<string> dependencies = [];

		try {
			string          content = await File.ReadAllTextAsync(filePath);
			MatchCollection matches = ImportRegex.Matches(content);

			foreach (Match match in matches) {
				string importPath = match.Groups[1].Value;
				if (!string.IsNullOrEmpty(importPath)) {
					dependencies.Add(importPath);
				}
			}
		} catch (Exception) {
			// Ignore file read errors
		}

		return dependencies.ToList();
	}
}

internal class GenericDependencyAnalyzer : IDependencyAnalyzer {
	public async Task<List<string>> GetDependenciesAsync(string filePath) {
		// Fallback analyzer that doesn't extract dependencies
		await Task.CompletedTask;
		return [];
	}
}

internal static class StringExtensions {
	public static bool IsNullOrEmpty(this string? str) => string.IsNullOrEmpty(str);
}