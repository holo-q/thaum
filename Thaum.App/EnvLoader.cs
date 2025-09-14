using System.Text.RegularExpressions;

namespace Thaum.Core.Utils;

public static class EnvLoader {
	private static readonly Regex _envLineRegex = new(@"^(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)$",
		RegexOptions.Compiled | RegexOptions.Multiline);

	public record EnvFile(string Path, Dictionary<string, string> Variables, bool Exists);

	public record EnvLoadResult(List<EnvFile> LoadedFiles, Dictionary<string, string> MergedVariables);

	/// <summary>
	/// Loads .env files walking up the directory tree and merges them down (parent variables override child variables)
	/// </summary>
	public static EnvLoadResult LoadEnvironmentFiles(string? startDirectory = null) {
		startDirectory ??= Directory.GetCurrentDirectory();
		List<EnvFile>              loadedFiles     = new List<EnvFile>();
		Dictionary<string, string> mergedVariables = new Dictionary<string, string>();

		List<string> directories = GetDirectoryHierarchy(startDirectory);

		// Process from root down to current directory (so current directory .env takes precedence)
		foreach (string directory in directories.AsEnumerable().Reverse()) {
			EnvFile envFile = LoadEnvFile(Path.Combine(directory, GLB.EnvFileName));
			loadedFiles.Add(envFile);

			// Merge variables - later files override earlier ones
			if (envFile.Exists) {
				foreach (KeyValuePair<string, string> kvp in envFile.Variables) {
					mergedVariables[kvp.Key] = kvp.Value;
				}
			}
		}

		return new EnvLoadResult(loadedFiles, mergedVariables);
	}

	/// <summary>
	/// Applies environment variables to the current process
	/// </summary>
	public static void ApplyToEnvironment(Dictionary<string, string> variables) {
		foreach (KeyValuePair<string, string> kvp in variables) {
			// Only set if not already present in actual environment variables
			if (Environment.GetEnvironmentVariable(kvp.Key) == null) {
				Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
			}
		}
	}

	/// <summary>
	/// Loads and applies .env files to current process environment
	/// </summary>
	public static EnvLoadResult LoadAndApply(string? startDirectory = null) {
		EnvLoadResult result = LoadEnvironmentFiles(startDirectory);
		ApplyToEnvironment(result.MergedVariables);
		return result;
	}

	private static List<string> GetDirectoryHierarchy(string startPath) {
		List<string> directories = new List<string>();
		string?      current     = Path.GetFullPath(startPath);

		while (current != null) {
			directories.Add(current);
			string? parent = Directory.GetParent(current)?.FullName;
			if (parent == current) break; // Reached root
			current = parent;
		}

		return directories;
	}

	private static EnvFile LoadEnvFile(string filePath) {
		Dictionary<string, string> variables = new Dictionary<string, string>();
		bool                       exists    = File.Exists(filePath);

		if (exists) {
			try {
				string          content = File.ReadAllText(filePath);
				MatchCollection matches = _envLineRegex.Matches(content);

				foreach (Match match in matches) {
					if (match.Success) {
						string key   = match.Groups["key"].Value;
						string value = match.Groups["value"].Value.Trim();

						// Handle quoted values
						if ((value.StartsWith('"') && value.EndsWith('"')) ||
						    (value.StartsWith('\'') && value.EndsWith('\''))) {
							value = value[1..^1];
						}

						// Handle escaped characters in double quotes
						if (match.Groups["value"].Value.Trim().StartsWith('"')) {
							value = value.Replace("\\n", "\n")
								.Replace("\\r", "\r")
								.Replace("\\t", "\t")
								.Replace("\\\"", "\"")
								.Replace(@"\\", "\\");
						}

						variables[key] = value;
					}
				}
			} catch (Exception) {
				// If we can't read the file, treat as non-existent
				exists = false;
			}
		}

		return new EnvFile(filePath, variables, exists);
	}

	/// <summary>
	/// Pretty print environment loading results with trace information
	/// </summary>
	public static void PrintLoadTrace(EnvLoadResult result, bool showValues = false) {
		Tracer.traceheader("ENVIRONMENT FILE DETECTION");

		List<EnvFile> foundFiles = result.LoadedFiles.Where(f => f.Exists).ToList();

		if (!foundFiles.Any()) {
			Tracer.traceln("No .env files found", "in directory hierarchy", "INFO");
		} else {
			// Show found files with cleaner paths
			foreach (EnvFile envFile in foundFiles) {
				string cleanPath = GetCleanPath(envFile.Path);
				Tracer.traceln("Found", cleanPath, "LOAD");

				if (envFile.Variables.Any()) {
					foreach (KeyValuePair<string, string> kvp in envFile.Variables.OrderBy(x => x.Key)) {
						if (showValues) {
							string displayValue = kvp.Value.Length > 40 ? $"{kvp.Value[..37]}..." : kvp.Value;
							Tracer.traceln($"  {kvp.Key}", displayValue, "VAR");
						} else {
							// Show key with safe preview using stars
							string preview = CreateSafePreview(kvp.Value);
							Tracer.traceln($"  {kvp.Key}", preview, "KEY");
						}
					}
				} else {
					Tracer.traceln($"  Empty file", "no variables", "EMPTY");
				}
			}
		}

		Tracer.traceheader("MERGED ENVIRONMENT");
		Tracer.traceln("Total Variables", $"{result.MergedVariables.Count} variables", "MERGE");

		if (result.MergedVariables.Any()) {
			foreach (KeyValuePair<string, string> kvp in result.MergedVariables.OrderBy(x => x.Key)) {
				if (showValues) {
					string displayValue = kvp.Value.Length > 50 ? $"{kvp.Value[..47]}..." : kvp.Value;
					Tracer.traceln(kvp.Key, displayValue, "ENV");
				} else {
					// Show key with safe preview using stars
					string preview = CreateSafePreview(kvp.Value);
					Tracer.traceln(kvp.Key, preview, "ENV");
				}
			}
		}
	}

	private static string GetCleanPath(string fullPath) {
		string currentDir   = Directory.GetCurrentDirectory();
		string relativePath = Path.GetRelativePath(currentDir, fullPath);

		// If it's just .env in current directory, show it as such
		if (relativePath == ".env")
			return ".env (current)";

		// Count directory levels up
		int upLevels = relativePath.Count(c => c == Path.DirectorySeparatorChar && relativePath.StartsWith(".."));
		if (upLevels > 0) {
			return $".env ({upLevels} level{(upLevels > 1 ? "s" : "")} up)";
		}

		// For subdirectories (shouldn't happen with our upward search, but just in case)
		return relativePath;
	}

	private static List<string> ChunkString(string text, int maxLength) {
		List<string> chunks = new List<string>();
		if (text.Length <= maxLength) {
			chunks.Add(text);
			return chunks;
		}

		string[] words        = text.Split(',');
		string   currentChunk = "";

		foreach (string word in words) {
			string wordWithComma = currentChunk.Length == 0 ? word.Trim() : $",{word}";
			if (currentChunk.Length + wordWithComma.Length <= maxLength) {
				currentChunk += wordWithComma;
			} else {
				if (currentChunk.Length > 0)
					chunks.Add(currentChunk);
				currentChunk = word.Trim();
			}
		}

		if (currentChunk.Length > 0)
			chunks.Add(currentChunk);

		return chunks;
	}

	private static string CreateSafePreview(string value) {
		if (string.IsNullOrEmpty(value))
			return "(empty)";

		int length = value.Length;

		switch (length) {
			// For very short values (like config values), show them as-is
			case <= 4:
				return value;
			// For typical API keys, show first 3-4 chars + stars + last 3-4 chars
			case >= 8: {
				string start     = value[..Math.Min(4, length / 3)];
				string end       = value[^Math.Min(4, length / 3)..];
				int    starCount = Math.Max(8, length - start.Length - end.Length);
				return $"{start}{'*'.ToString().PadRight(starCount, '*')}{end}";
			}
		}

		// For medium length values, show first few chars + stars
		int    visibleChars = Math.Max(1, length / 4);
		string prefix       = value[..visibleChars];
		string stars        = "*".PadRight(length - visibleChars, '*');
		return $"{prefix}{stars}";
	}
}