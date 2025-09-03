using System.Text.RegularExpressions;

namespace Thaum.Core.Utils;

public static class EnvironmentLoader
{
    private static readonly Regex EnvLineRegex = new(@"^(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)$", 
        RegexOptions.Compiled | RegexOptions.Multiline);

    public record EnvFile(string Path, Dictionary<string, string> Variables, bool Exists);
    public record EnvLoadResult(List<EnvFile> LoadedFiles, Dictionary<string, string> MergedVariables);

    /// <summary>
    /// Loads .env files walking up the directory tree and merges them down (parent variables override child variables)
    /// </summary>
    public static EnvLoadResult LoadEnvironmentFiles(string? startDirectory = null)
    {
        startDirectory ??= Directory.GetCurrentDirectory();
        var loadedFiles = new List<EnvFile>();
        var mergedVariables = new Dictionary<string, string>();
        
        var directories = GetDirectoryHierarchy(startDirectory);
        
        // Process from root down to current directory (so current directory .env takes precedence)
        foreach (var directory in directories.AsEnumerable().Reverse())
        {
            var envFile = LoadEnvFile(Path.Combine(directory, ".env"));
            loadedFiles.Add(envFile);
            
            // Merge variables - later files override earlier ones
            if (envFile.Exists)
            {
                foreach (var kvp in envFile.Variables)
                {
                    mergedVariables[kvp.Key] = kvp.Value;
                }
            }
        }
        
        return new EnvLoadResult(loadedFiles, mergedVariables);
    }
    
    /// <summary>
    /// Applies environment variables to the current process
    /// </summary>
    public static void ApplyToEnvironment(Dictionary<string, string> variables)
    {
        foreach (var kvp in variables)
        {
            // Only set if not already present in actual environment variables
            if (Environment.GetEnvironmentVariable(kvp.Key) == null)
            {
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }
    }
    
    /// <summary>
    /// Loads and applies .env files to current process environment
    /// </summary>
    public static EnvLoadResult LoadAndApply(string? startDirectory = null)
    {
        var result = LoadEnvironmentFiles(startDirectory);
        ApplyToEnvironment(result.MergedVariables);
        return result;
    }
    
    private static List<string> GetDirectoryHierarchy(string startPath)
    {
        var directories = new List<string>();
        var current = Path.GetFullPath(startPath);
        
        while (current != null)
        {
            directories.Add(current);
            var parent = Directory.GetParent(current)?.FullName;
            if (parent == current) break; // Reached root
            current = parent;
        }
        
        return directories;
    }
    
    private static EnvFile LoadEnvFile(string filePath)
    {
        var variables = new Dictionary<string, string>();
        var exists = File.Exists(filePath);
        
        if (exists)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var matches = EnvLineRegex.Matches(content);
                
                foreach (Match match in matches)
                {
                    if (match.Success)
                    {
                        var key = match.Groups["key"].Value;
                        var value = match.Groups["value"].Value.Trim();
                        
                        // Handle quoted values
                        if ((value.StartsWith('"') && value.EndsWith('"')) ||
                            (value.StartsWith('\'') && value.EndsWith('\'')))
                        {
                            value = value[1..^1];
                        }
                        
                        // Handle escaped characters in double quotes
                        if (match.Groups["value"].Value.Trim().StartsWith('"'))
                        {
                            value = value.Replace("\\n", "\n")
                                        .Replace("\\r", "\r")
                                        .Replace("\\t", "\t")
                                        .Replace("\\\"", "\"")
                                        .Replace("\\\\", "\\");
                        }
                        
                        variables[key] = value;
                    }
                }
            }
            catch (Exception)
            {
                // If we can't read the file, treat as non-existent
                exists = false;
            }
        }
        
        return new EnvFile(filePath, variables, exists);
    }
    
    /// <summary>
    /// Pretty print environment loading results with trace information
    /// </summary>
    public static void PrintLoadTrace(EnvLoadResult result, bool showValues = false)
    {
        TraceFormatter.PrintHeader("ENVIRONMENT FILE DETECTION");
        
        var foundFiles = result.LoadedFiles.Where(f => f.Exists).ToList();
        
        if (!foundFiles.Any())
        {
            TraceFormatter.PrintTrace("No .env files found", "in directory hierarchy", "INFO");
        }
        else
        {
            // Show found files with cleaner paths
            foreach (var envFile in foundFiles)
            {
                var cleanPath = GetCleanPath(envFile.Path);
                TraceFormatter.PrintTrace("Found", cleanPath, "LOAD");
                
                if (envFile.Variables.Any())
                {
                    if (showValues)
                    {
                        foreach (var kvp in envFile.Variables.OrderBy(x => x.Key))
                        {
                            var displayValue = kvp.Value.Length > 40 ? kvp.Value[..37] + "..." : kvp.Value;
                            TraceFormatter.PrintTrace($"  {kvp.Key}", displayValue, "VAR");
                        }
                    }
                    else
                    {
                        // Show just the keys without values
                        var keys = string.Join(", ", envFile.Variables.Keys.OrderBy(x => x));
                        var keyDisplay = keys.Length > 60 ? keys[..57] + "..." : keys;
                        TraceFormatter.PrintTrace($"  Keys", keyDisplay, "KEYS");
                    }
                }
                else
                {
                    TraceFormatter.PrintTrace($"  Empty file", "no variables", "EMPTY");
                }
            }
        }
        
        TraceFormatter.PrintHeader("MERGED ENVIRONMENT");
        TraceFormatter.PrintTrace("Total Variables", $"{result.MergedVariables.Count} variables", "MERGE");
        
        if (result.MergedVariables.Any())
        {
            if (showValues)
            {
                foreach (var kvp in result.MergedVariables.OrderBy(x => x.Key))
                {
                    var displayValue = kvp.Value.Length > 50 ? kvp.Value[..47] + "..." : kvp.Value;
                    TraceFormatter.PrintTrace(kvp.Key, displayValue, "ENV");
                }
            }
            else
            {
                // Show just the merged keys
                var allKeys = string.Join(", ", result.MergedVariables.Keys.OrderBy(x => x));
                var keyChunks = ChunkString(allKeys, 70);
                foreach (var chunk in keyChunks)
                {
                    TraceFormatter.PrintTrace("Keys", chunk, "ENV");
                }
            }
        }
    }
    
    private static string GetCleanPath(string fullPath)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var relativePath = Path.GetRelativePath(currentDir, fullPath);
        
        // If it's just .env in current directory, show it as such
        if (relativePath == ".env")
            return ".env (current)";
        
        // Count directory levels up
        var upLevels = relativePath.Count(c => c == Path.DirectorySeparatorChar && relativePath.StartsWith(".."));
        if (upLevels > 0)
        {
            return $".env ({upLevels} level{(upLevels > 1 ? "s" : "")} up)";
        }
        
        // For subdirectories (shouldn't happen with our upward search, but just in case)
        return relativePath;
    }
    
    private static List<string> ChunkString(string text, int maxLength)
    {
        var chunks = new List<string>();
        if (text.Length <= maxLength)
        {
            chunks.Add(text);
            return chunks;
        }
        
        var words = text.Split(',');
        var currentChunk = "";
        
        foreach (var word in words)
        {
            var wordWithComma = currentChunk.Length == 0 ? word.Trim() : "," + word;
            if (currentChunk.Length + wordWithComma.Length <= maxLength)
            {
                currentChunk += wordWithComma;
            }
            else
            {
                if (currentChunk.Length > 0)
                    chunks.Add(currentChunk);
                currentChunk = word.Trim();
            }
        }
        
        if (currentChunk.Length > 0)
            chunks.Add(currentChunk);
            
        return chunks;
    }
}