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
        var missingFiles = result.LoadedFiles.Where(f => !f.Exists).ToList();
        
        // Show found files
        foreach (var envFile in foundFiles)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), envFile.Path);
            TraceFormatter.PrintTrace("Found", relativePath, "LOAD");
            
            if (showValues)
            {
                foreach (var kvp in envFile.Variables)
                {
                    var displayValue = kvp.Value.Length > 40 ? kvp.Value[..37] + "..." : kvp.Value;
                    TraceFormatter.PrintTrace($"  {kvp.Key}", displayValue, "VAR");
                }
            }
            else
            {
                TraceFormatter.PrintTrace($"  Variables", $"{envFile.Variables.Count} found", "COUNT");
            }
        }
        
        // Show missing files
        foreach (var envFile in missingFiles)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), envFile.Path);
            TraceFormatter.PrintTrace("Missing", relativePath, "SKIP");
        }
        
        TraceFormatter.PrintHeader("MERGED ENVIRONMENT");
        TraceFormatter.PrintTrace("Total Variables", $"{result.MergedVariables.Count} variables", "MERGE");
        
        if (showValues)
        {
            foreach (var kvp in result.MergedVariables.OrderBy(x => x.Key))
            {
                var displayValue = kvp.Value.Length > 50 ? kvp.Value[..47] + "..." : kvp.Value;
                TraceFormatter.PrintTrace(kvp.Key, displayValue, "ENV");
            }
        }
    }
}