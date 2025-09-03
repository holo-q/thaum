using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

// Simplified LSP client manager for initial implementation
public class SimpleLspClientManager : ILspClientManager
{
    private readonly ILogger<SimpleLspClientManager> _logger;
    private readonly Dictionary<string, bool> _runningServers = new();

    public SimpleLspClientManager(ILogger<SimpleLspClientManager> logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartLanguageServerAsync(string language, string workspacePath)
    {
        _logger.LogInformation("Starting {Language} language server for {WorkspacePath}", language, workspacePath);
        
        // Simulate starting language server
        await Task.Delay(100);
        _runningServers[language] = true;
        
        return true;
    }

    public async Task<bool> StopLanguageServerAsync(string language)
    {
        _logger.LogInformation("Stopping {Language} language server", language);
        
        await Task.Delay(50);
        _runningServers.Remove(language);
        
        return true;
    }

    public async Task<List<CodeSymbol>> GetWorkspaceSymbolsAsync(string language, string workspacePath)
    {
        if (!IsLanguageServerRunning(language))
        {
            return new List<CodeSymbol>();
        }

        // Simple file-based symbol extraction
        var symbols = new List<CodeSymbol>();
        
        try
        {
            var sourceFiles = Directory.GetFiles(workspacePath, "*.*", SearchOption.AllDirectories)
                .Where(f => IsSourceFileForLanguage(f, language))
                .Take(20) // Limit for performance
                .ToList();

            foreach (var file in sourceFiles)
            {
                var fileSymbols = await ExtractSymbolsFromFile(file, language);
                symbols.AddRange(fileSymbols);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting workspace symbols");
        }

        return symbols;
    }

    public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string language, string filePath)
    {
        if (!IsLanguageServerRunning(language) || !File.Exists(filePath))
        {
            return new List<CodeSymbol>();
        }

        return await ExtractSymbolsFromFile(filePath, language);
    }

    public async Task<string?> GetSymbolDefinitionAsync(string language, string filePath, ThaumPosition position)
    {
        await Task.CompletedTask;
        return filePath; // Simple implementation returns current file
    }

    public async Task<List<string>> GetSymbolReferencesAsync(string language, string filePath, ThaumPosition position)
    {
        await Task.CompletedTask;
        return new List<string> { filePath }; // Simple implementation
    }

    public bool IsLanguageServerRunning(string language)
    {
        return _runningServers.ContainsKey(language);
    }

    public async IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges(string language, string workspacePath)
    {
        // Placeholder implementation
        await Task.CompletedTask;
        yield break;
    }

    private async Task<List<CodeSymbol>> ExtractSymbolsFromFile(string filePath, string language)
    {
        var symbols = new List<CodeSymbol>();
        
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            var lines = content.Split('\n');
            
            // Simple pattern-based extraction based on language
            switch (language.ToLowerInvariant())
            {
                case "python":
                    ExtractPythonSymbols(symbols, lines, filePath);
                    break;
                case "csharp":
                    ExtractCSharpSymbols(symbols, lines, filePath);
                    break;
                default:
                    ExtractGenericSymbols(symbols, lines, filePath);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting symbols from {FilePath}", filePath);
        }

        return symbols;
    }

    private void ExtractPythonSymbols(List<CodeSymbol> symbols, string[] lines, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            if (line.StartsWith("def ") && line.Contains('('))
            {
                var name = ExtractFunctionName(line, "def ");
                symbols.Add(new CodeSymbol(
                    Name: name,
                    Kind: SymbolKind.Function,
                    FilePath: filePath,
                    StartPosition: new ThaumPosition(i, 0),
                    EndPosition: new ThaumPosition(i, line.Length)
                ));
            }
            else if (line.StartsWith("class ") && line.Contains(':'))
            {
                var name = ExtractClassName(line, "class ");
                symbols.Add(new CodeSymbol(
                    Name: name,
                    Kind: SymbolKind.Class,
                    FilePath: filePath,
                    StartPosition: new ThaumPosition(i, 0),
                    EndPosition: new ThaumPosition(i, line.Length)
                ));
            }
        }
    }

    private void ExtractCSharpSymbols(List<CodeSymbol> symbols, string[] lines, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            // Skip comments and empty lines
            if (line.StartsWith("//") || string.IsNullOrEmpty(line))
                continue;
                
            // Match methods/functions with proper C# syntax (not records, properties, etc.)
            if ((line.StartsWith("public ") || line.StartsWith("private ") || 
                 line.StartsWith("protected ") || line.StartsWith("internal ")) &&
                line.Contains('(') && line.Contains(')') && 
                !line.Contains("class ") && !line.Contains("interface ") &&
                !line.Contains("record ") && !line.Contains("enum ") &&
                !line.Contains("=") && // Skip property assignments
                !line.Contains("new ") && // Skip constructors called with 'new'
                !line.Contains("get;") && !line.Contains("set;") && // Skip properties
                !line.Contains(" => ") && // Skip expression-bodied members
                (line.Contains("void ") || line.Contains("async ") || line.Contains("Task") || 
                 line.Contains("string ") || line.Contains("int ") || line.Contains("bool ") ||
                 line.Contains("List<") || line.Contains("IAsync") || line.Contains("public static"))) // Must return something or be static
            {
                var name = ExtractMethodName(line);
                if (!string.IsNullOrEmpty(name) && name.Length > 1 && 
                    char.IsLetter(name[0])) // Must start with letter
                {
                    symbols.Add(new CodeSymbol(
                        Name: name,
                        Kind: SymbolKind.Method,
                        FilePath: filePath,
                        StartPosition: new ThaumPosition(i, 0),
                        EndPosition: new ThaumPosition(i, line.Length)
                    ));
                }
            }
            // Match class declarations
            else if ((line.StartsWith("public class ") || line.StartsWith("internal class ") ||
                     line.StartsWith("class ")) && !line.Contains("//"))
            {
                var name = ExtractCSharpClassName(line);
                if (!string.IsNullOrEmpty(name) && name.Length > 1 && 
                    char.IsLetter(name[0])) // Must start with letter
                {
                    symbols.Add(new CodeSymbol(
                        Name: name,
                        Kind: SymbolKind.Class,
                        FilePath: filePath,
                        StartPosition: new ThaumPosition(i, 0),
                        EndPosition: new ThaumPosition(i, line.Length)
                    ));
                }
            }
        }
    }

    private void ExtractGenericSymbols(List<CodeSymbol> symbols, string[] lines, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            
            if (line.Contains("function ") && line.Contains('('))
            {
                var name = ExtractFunctionName(line, "function ");
                symbols.Add(new CodeSymbol(
                    Name: name,
                    Kind: SymbolKind.Function,
                    FilePath: filePath,
                    StartPosition: new ThaumPosition(i, 0),
                    EndPosition: new ThaumPosition(i, line.Length)
                ));
            }
        }
    }

    private string ExtractFunctionName(string line, string prefix)
    {
        var start = line.IndexOf(prefix) + prefix.Length;
        var end = line.IndexOf('(', start);
        return end > start ? line[start..end].Trim() : "Unknown";
    }

    private string ExtractClassName(string line, string prefix)
    {
        var start = line.IndexOf(prefix) + prefix.Length;
        var end = line.IndexOfAny(new[] { ':', '(', '{' }, start);
        if (end == -1) end = line.Length;
        return end > start ? line[start..end].Trim() : "Unknown";
    }

    private string ExtractMethodName(string line)
    {
        var openParen = line.IndexOf('(');
        if (openParen == -1) return "";
        
        var words = line[..openParen].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[^1] : "";
    }

    private string ExtractCSharpClassName(string line)
    {
        var classIndex = line.IndexOf("class ");
        if (classIndex == -1) return "";
        
        var start = classIndex + 6;
        var end = line.IndexOfAny(new[] { ' ', ':', '{', '<' }, start);
        if (end == -1) end = line.Length;
        
        return end > start ? line[start..end].Trim() : "";
    }

    private static bool IsSourceFileForLanguage(string filePath, string language)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        return language.ToLowerInvariant() switch
        {
            "python" => extension == ".py",
            "csharp" => extension == ".cs",
            "javascript" => extension is ".js" or ".jsx",
            "typescript" => extension is ".ts" or ".tsx",
            "rust" => extension == ".rs",
            "go" => extension == ".go",
            _ => false
        };
    }

    public void Dispose()
    {
        foreach (var language in _runningServers.Keys.ToList())
        {
            StopLanguageServerAsync(language).Wait();
        }
    }
}