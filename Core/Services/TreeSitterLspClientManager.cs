using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

/// <summary>
/// LSP Client Manager that uses TreeSitter for robust parsing of multiple languages
/// Currently implements fallback regex parsing - TreeSitter integration to be completed
/// </summary>
public class TreeSitterLspClientManager : ILspClientManager
{
    private readonly ILogger<TreeSitterLspClientManager> _logger;
    private readonly Dictionary<string, TreeSitterLanguageConfig> _languageConfigs;
    
    public TreeSitterLspClientManager(ILogger<TreeSitterLspClientManager> logger)
    {
        _logger = logger;
        _languageConfigs = InitializeLanguageConfigs();
    }

    public async Task<bool> StartLanguageServerAsync(string language, string workspacePath)
    {
        _logger.LogInformation("Starting TreeSitter parser for {Language} at {WorkspacePath}", language, workspacePath);
        
        if (!_languageConfigs.ContainsKey(language.ToLowerInvariant()))
        {
            _logger.LogWarning("No TreeSitter configuration found for language: {Language}", language);
            return false;
        }

        return true;
    }

    public async Task<bool> StopLanguageServerAsync(string language)
    {
        _logger.LogInformation("Stopping TreeSitter parser for {Language}", language);
        return true;
    }

    public async Task<List<CodeSymbol>> GetWorkspaceSymbolsAsync(string language, string workspacePath)
    {
        var symbols = new List<CodeSymbol>();
        
        if (!_languageConfigs.TryGetValue(language.ToLowerInvariant(), out var config))
        {
            _logger.LogWarning("No TreeSitter configuration for language: {Language}", language);
            return symbols;
        }

        try
        {
            // Find all source files for the language
            var sourceFiles = Directory.GetFiles(workspacePath, "*", SearchOption.AllDirectories)
                .Where(file => config.FileExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _logger.LogDebug("Found {Count} {Language} files to parse", sourceFiles.Count, language);

            foreach (var filePath in sourceFiles)
            {
                try
                {
                    var fileSymbols = await ParseFileSymbols(filePath, config);
                    symbols.AddRange(fileSymbols);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse file: {FilePath}", filePath);
                }
            }

            _logger.LogDebug("Extracted {Count} symbols from {Language} files", symbols.Count, language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning workspace for {Language} symbols", language);
        }

        return symbols;
    }

    public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string language, string filePath)
    {
        if (!_languageConfigs.TryGetValue(language.ToLowerInvariant(), out var config))
        {
            return new List<CodeSymbol>();
        }

        try
        {
            return await ParseFileSymbols(filePath, config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing document symbols for {FilePath}", filePath);
            return new List<CodeSymbol>();
        }
    }

    public async Task<string?> GetSymbolDefinitionAsync(string language, string filePath, ThaumPosition position)
    {
        return await Task.FromResult(filePath);
    }

    public async Task<List<string>> GetSymbolReferencesAsync(string language, string filePath, ThaumPosition position)
    {
        return await Task.FromResult(new List<string> { filePath });
    }

    public bool IsLanguageServerRunning(string language)
    {
        return _languageConfigs.ContainsKey(language.ToLowerInvariant());
    }

    public async IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges(string language, string workspacePath)
    {
        yield break;
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }

    private async Task<List<CodeSymbol>> ParseFileSymbols(string filePath, TreeSitterLanguageConfig config)
    {
        var symbols = new List<CodeSymbol>();
        
        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var lines = sourceCode.Split('\n');
            
            // Use language-specific parsing
            switch (config.Language)
            {
                case "csharp":
                    ExtractCSharpSymbols(symbols, lines, filePath);
                    break;
                case "python":
                    ExtractPythonSymbols(symbols, lines, filePath);
                    break;
                case "javascript":
                case "typescript":
                    ExtractJavaScriptSymbols(symbols, lines, filePath);
                    break;
                default:
                    ExtractGenericSymbols(symbols, lines, filePath);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing file: {FilePath}", filePath);
        }

        return symbols;
    }

    private void ExtractCSharpSymbols(List<CodeSymbol> symbols, string[] lines, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip comments and empty lines
            if (line.StartsWith("//") || string.IsNullOrEmpty(line))
                continue;

            // Class declarations
            if ((line.Contains("class ") || line.Contains("interface ") || line.Contains("struct ")) && 
                !line.Contains("//"))
            {
                var className = ExtractCSharpClassName(line);
                if (!string.IsNullOrEmpty(className))
                {
                    symbols.Add(new CodeSymbol(
                        Name: className,
                        Kind: line.Contains("interface ") ? SymbolKind.Interface : SymbolKind.Class,
                        FilePath: filePath,
                        StartPosition: new ThaumPosition(i, 0),
                        EndPosition: new ThaumPosition(i, line.Length)
                    ));
                }
            }

            // Method declarations
            if ((line.Contains("public ") || line.Contains("private ") || 
                line.Contains("protected ") || line.Contains("internal ")) &&
                line.Contains('(') && line.Contains(')') &&
                !line.Contains("class ") && !line.Contains("interface ") &&
                !line.Contains("record ") && !line.Contains("enum ") &&
                !line.Contains("=") && !line.Contains("new ") &&
                !line.Contains("get;") && !line.Contains("set;") &&
                !line.Contains(" => "))
            {
                var methodName = ExtractMethodName(line);
                if (!string.IsNullOrEmpty(methodName))
                {
                    symbols.Add(new CodeSymbol(
                        Name: methodName,
                        Kind: SymbolKind.Method,
                        FilePath: filePath,
                        StartPosition: new ThaumPosition(i, 0),
                        EndPosition: new ThaumPosition(i, line.Length)
                    ));
                }
            }
        }
    }

    private void ExtractPythonSymbols(List<CodeSymbol> symbols, string[] lines, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip comments and empty lines
            if (line.StartsWith("#") || string.IsNullOrEmpty(line))
                continue;

            // Function declarations
            if (line.StartsWith("def ") && line.Contains('('))
            {
                var name = ExtractFunctionName(line, "def ");
                if (!string.IsNullOrEmpty(name))
                {
                    symbols.Add(new CodeSymbol(
                        Name: name,
                        Kind: SymbolKind.Function,
                        FilePath: filePath,
                        StartPosition: new ThaumPosition(i, 0),
                        EndPosition: new ThaumPosition(i, line.Length)
                    ));
                }
            }

            // Class declarations
            if (line.StartsWith("class ") && line.Contains(':'))
            {
                var name = ExtractClassName(line, "class ");
                if (!string.IsNullOrEmpty(name))
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

    private void ExtractJavaScriptSymbols(List<CodeSymbol> symbols, string[] lines, string filePath)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            // Skip comments and empty lines
            if (line.StartsWith("//") || line.StartsWith("/*") || string.IsNullOrEmpty(line))
                continue;

            // Function declarations
            if (line.Contains("function ") || (line.Contains("const ") && line.Contains("=>")) ||
                (line.Contains("let ") && line.Contains("=>")))
            {
                var name = ExtractJavaScriptFunctionName(line);
                if (!string.IsNullOrEmpty(name))
                {
                    symbols.Add(new CodeSymbol(
                        Name: name,
                        Kind: SymbolKind.Function,
                        FilePath: filePath,
                        StartPosition: new ThaumPosition(i, 0),
                        EndPosition: new ThaumPosition(i, line.Length)
                    ));
                }
            }

            // Class declarations
            if (line.StartsWith("class ") || line.Contains("export class "))
            {
                var name = ExtractClassName(line, "class ");
                if (!string.IsNullOrEmpty(name))
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

            // Skip empty lines
            if (string.IsNullOrEmpty(line))
                continue;

            // Generic function pattern
            if (line.Contains("function ") || line.Contains("def "))
            {
                var name = ExtractGenericFunctionName(line);
                if (!string.IsNullOrEmpty(name))
                {
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
    }

    // Helper methods
    private string ExtractCSharpClassName(string line)
    {
        var patterns = new[] { "class ", "interface ", "struct " };
        foreach (var pattern in patterns)
        {
            var index = line.IndexOf(pattern);
            if (index >= 0)
            {
                var nameStart = index + pattern.Length;
                var remaining = line[nameStart..].Trim();
                var nameEnd = remaining.IndexOfAny(new[] { ' ', ':', '<', '{' });
                return nameEnd > 0 ? remaining[..nameEnd] : remaining;
            }
        }
        return string.Empty;
    }

    private string ExtractMethodName(string line)
    {
        var parenIndex = line.IndexOf('(');
        if (parenIndex < 0) return string.Empty;

        var beforeParen = line[..parenIndex].Trim();
        var words = beforeParen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 ? words[^1] : string.Empty;
    }

    private string ExtractFunctionName(string line, string keyword)
    {
        var keywordIndex = line.IndexOf(keyword);
        if (keywordIndex < 0) return string.Empty;

        var nameStart = keywordIndex + keyword.Length;
        var remaining = line[nameStart..].Trim();
        var nameEnd = remaining.IndexOf('(');
        return nameEnd > 0 ? remaining[..nameEnd] : remaining;
    }

    private string ExtractClassName(string line, string keyword)
    {
        var keywordIndex = line.IndexOf(keyword);
        if (keywordIndex < 0) return string.Empty;

        var nameStart = keywordIndex + keyword.Length;
        var remaining = line[nameStart..].Trim();
        var nameEnd = remaining.IndexOfAny(new[] { '(', ':', ' ', '{' });
        return nameEnd > 0 ? remaining[..nameEnd] : remaining;
    }

    private string ExtractJavaScriptFunctionName(string line)
    {
        // function name() pattern
        if (line.Contains("function "))
        {
            return ExtractFunctionName(line, "function ");
        }
        
        // const name = () => pattern
        if (line.Contains("const ") && line.Contains("=>"))
        {
            var constIndex = line.IndexOf("const ");
            var nameStart = constIndex + 6;
            var remaining = line[nameStart..].Trim();
            var nameEnd = remaining.IndexOfAny(new[] { ' ', '=' });
            return nameEnd > 0 ? remaining[..nameEnd] : remaining;
        }

        return string.Empty;
    }

    private string ExtractGenericFunctionName(string line)
    {
        if (line.Contains("function "))
            return ExtractFunctionName(line, "function ");
        if (line.Contains("def "))
            return ExtractFunctionName(line, "def ");
        return string.Empty;
    }

    private Dictionary<string, TreeSitterLanguageConfig> InitializeLanguageConfigs()
    {
        return new Dictionary<string, TreeSitterLanguageConfig>
        {
            ["csharp"] = new TreeSitterLanguageConfig
            {
                Language = "csharp",
                FileExtensions = new[] { ".cs" }
            },
            ["python"] = new TreeSitterLanguageConfig
            {
                Language = "python",
                FileExtensions = new[] { ".py" }
            },
            ["javascript"] = new TreeSitterLanguageConfig
            {
                Language = "javascript",
                FileExtensions = new[] { ".js", ".jsx" }
            },
            ["typescript"] = new TreeSitterLanguageConfig
            {
                Language = "typescript", 
                FileExtensions = new[] { ".ts", ".tsx" }
            },
            ["rust"] = new TreeSitterLanguageConfig
            {
                Language = "rust",
                FileExtensions = new[] { ".rs" }
            },
            ["go"] = new TreeSitterLanguageConfig
            {
                Language = "go",
                FileExtensions = new[] { ".go" }
            }
        };
    }
}

public record TreeSitterLanguageConfig
{
    public required string Language { get; init; }
    public required string[] FileExtensions { get; init; }
}