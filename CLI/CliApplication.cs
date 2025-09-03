using Microsoft.Extensions.Logging;
using Thaum.Core.Services;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using Terminal.Gui;

namespace Thaum.CLI;

public class CliApplication
{
    private readonly SimpleLspClientManager _lspManager;
    private readonly ILogger<CliApplication> _logger;
    private readonly PerceptualColorEngine _colorEngine;

    public CliApplication(ILogger<CliApplication> logger)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _lspManager = new SimpleLspClientManager(loggerFactory.CreateLogger<SimpleLspClientManager>());
        _logger = logger;
        _colorEngine = new PerceptualColorEngine();
    }

    public async Task RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return;
        }

        var command = args[0].ToLowerInvariant();

        switch (command)
        {
            case "ls":
                await HandleLsCommand(args);
                break;
            case "summarize":
                await HandleSummarizeCommand(args);
                break;
            case "help":
            case "--help":
            case "-h":
                ShowHelp();
                break;
            default:
                Console.WriteLine($"Unknown command: {command}");
                ShowHelp();
                Environment.Exit(1);
                break;
        }
    }

    private async Task HandleLsCommand(string[] args)
    {
        var options = ParseLsOptions(args);
        
        Console.WriteLine($"Scanning {options.ProjectPath} for {options.Language} symbols...");

        try
        {
            // Start language server
            var started = await _lspManager.StartLanguageServerAsync(options.Language, options.ProjectPath);
            if (!started)
            {
                Console.WriteLine($"Failed to start {options.Language} language server");
                Environment.Exit(1);
            }

            // Get symbols
            var symbols = await _lspManager.GetWorkspaceSymbolsAsync(options.Language, options.ProjectPath);
            
            if (!symbols.Any())
            {
                Console.WriteLine("No symbols found.");
                return;
            }

            // Build and display hierarchy
            var hierarchy = BuildHierarchy(symbols);
            DisplayHierarchy(hierarchy, options);

            Console.WriteLine($"\nFound {symbols.Count} symbols total");
        }
        finally
        {
            await _lspManager.StopLanguageServerAsync(options.Language);
        }
    }

    private async Task HandleSummarizeCommand(string[] args)
    {
        var options = ParseSummarizeOptions(args);
        
        Console.WriteLine($"Summarizing {options.ProjectPath} ({options.Language})...");
        Console.WriteLine("Note: Summarization not yet implemented in simplified version.");
    }

    private LsOptions ParseLsOptions(string[] args)
    {
        var projectPath = Directory.GetCurrentDirectory();
        var language = "auto";
        var maxDepth = 10;
        var showTypes = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--path" when i + 1 < args.Length:
                    projectPath = args[++i];
                    break;
                case "--lang" when i + 1 < args.Length:
                    language = args[++i];
                    break;
                case "--depth" when i + 1 < args.Length:
                    maxDepth = int.Parse(args[++i]);
                    break;
                case "--types":
                    showTypes = true;
                    break;
                default:
                    if (!args[i].StartsWith("--"))
                    {
                        projectPath = args[i];
                    }
                    break;
            }
        }

        // Auto-detect language if not specified
        if (language == "auto")
        {
            language = DetectLanguage(projectPath);
        }

        return new LsOptions(projectPath, language, maxDepth, showTypes);
    }

    private SummarizeOptions ParseSummarizeOptions(string[] args)
    {
        var projectPath = Directory.GetCurrentDirectory();
        var language = "auto";

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--path" when i + 1 < args.Length:
                    projectPath = args[++i];
                    break;
                case "--lang" when i + 1 < args.Length:
                    language = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith("--"))
                    {
                        projectPath = args[i];
                    }
                    break;
            }
        }

        if (language == "auto")
        {
            language = DetectLanguage(projectPath);
        }

        return new SummarizeOptions(projectPath, language);
    }

    private string DetectLanguage(string projectPath)
    {
        var files = Directory.GetFiles(projectPath, "*.*", SearchOption.TopDirectoryOnly);
        var extensions = files.Select(Path.GetExtension).Where(ext => !string.IsNullOrEmpty(ext));
        
        var counts = extensions.GroupBy(ext => ext.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        // Check for specific project files first
        if (File.Exists(Path.Combine(projectPath, "pyproject.toml")) || 
            File.Exists(Path.Combine(projectPath, "requirements.txt")) ||
            counts.GetValueOrDefault(".py", 0) > 0)
            return "python";

        if (File.Exists(Path.Combine(projectPath, "*.csproj")) || 
            File.Exists(Path.Combine(projectPath, "*.sln")) ||
            counts.GetValueOrDefault(".cs", 0) > 0)
            return "csharp";

        if (File.Exists(Path.Combine(projectPath, "package.json")))
        {
            if (counts.GetValueOrDefault(".ts", 0) > counts.GetValueOrDefault(".js", 0))
                return "typescript";
            return "javascript";
        }

        if (File.Exists(Path.Combine(projectPath, "Cargo.toml")))
            return "rust";

        if (File.Exists(Path.Combine(projectPath, "go.mod")))
            return "go";

        // Fallback to most common extension
        var mostCommon = counts.OrderByDescending(kv => kv.Value).FirstOrDefault();
        return mostCommon.Key switch
        {
            ".py" => "python",
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".rs" => "rust",
            ".go" => "go",
            _ => "python" // Default fallback
        };
    }

    private List<HierarchyNode> BuildHierarchy(List<CodeSymbol> symbols)
    {
        var nodes = new List<HierarchyNode>();
        var symbolsByFile = symbols.GroupBy(s => s.FilePath);

        foreach (var fileGroup in symbolsByFile)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fileGroup.Key);
            var fileNode = new HierarchyNode(relativePath, SymbolKind.Module, null);
            
            // Only include classes and functions/methods
            var filteredSymbols = fileGroup.Where(s => 
                s.Kind == SymbolKind.Class || 
                s.Kind == SymbolKind.Function || 
                s.Kind == SymbolKind.Method).ToList();
            
            if (filteredSymbols.Any())
            {
                foreach (var symbol in filteredSymbols.OrderBy(s => s.StartPosition.Line))
                {
                    var symbolNode = new HierarchyNode(symbol.Name, symbol.Kind, symbol);
                    fileNode.Children.Add(symbolNode);
                    
                    // Add nested symbols if any (only classes and functions)
                    if (symbol.Children?.Any() == true)
                    {
                        AddChildSymbols(symbolNode, symbol.Children);
                    }
                }
                
                nodes.Add(fileNode);
            }
        }

        return nodes.OrderBy(n => n.Name).ToList();
    }

    private void AddChildSymbols(HierarchyNode parent, List<CodeSymbol> children)
    {
        // Only include classes and functions/methods
        var filteredChildren = children.Where(c => 
            c.Kind == SymbolKind.Class || 
            c.Kind == SymbolKind.Function || 
            c.Kind == SymbolKind.Method).ToList();
            
        foreach (var child in filteredChildren.OrderBy(c => c.StartPosition.Line))
        {
            var childNode = new HierarchyNode(child.Name, child.Kind, child);
            parent.Children.Add(childNode);
            
            if (child.Children?.Any() == true)
            {
                AddChildSymbols(childNode, child.Children);
            }
        }
    }

    private void DisplayHierarchy(List<HierarchyNode> nodes, LsOptions options)
    {
        foreach (var node in nodes)
        {
            DisplayNodeGrouped(node, "", true, options, 0);
        }
    }

    private void DisplayNodeGrouped(HierarchyNode node, string prefix, bool isLast, LsOptions options, int depth)
    {
        if (depth >= options.MaxDepth) return;

        var connector = isLast ? "└── " : "├── ";
        var symbol = GetSymbolIcon(node.Kind);
        
        Console.WriteLine($"{prefix}{connector}{symbol} {node.Name}");
        
        if (node.Children.Any())
        {
            var newPrefix = prefix + (isLast ? "    " : "│   ");
            
            // Group children by type
            var groupedChildren = node.Children.GroupBy(c => c.Kind).ToList();
            
            foreach (var group in groupedChildren)
            {
                var isLastGroup = group == groupedChildren.Last();
                DisplaySymbolGroup(group.Key, group.ToList(), newPrefix, isLastGroup, options, depth + 1);
            }
        }
    }

    private void DisplaySymbolGroup(SymbolKind kind, List<HierarchyNode> symbols, string prefix, bool isLast, LsOptions options, int depth)
    {
        if (depth >= options.MaxDepth || !symbols.Any()) return;

        var connector = isLast ? "└── " : "├── ";
        var icon = GetSymbolIcon(kind);
        var kindName = GetKindDisplayName(kind);
        
        // Print prefix and label normally
        var linePrefix = $"{prefix}{connector}{icon} {kindName}: ";
        Console.Write(linePrefix);
        
        // Print symbols with Terminal.Gui colors
        PrintColoredSymbols(symbols, kind, 80 - linePrefix.Length);
        
        Console.WriteLine(); // End the line
    }

    private void PrintColoredSymbols(List<HierarchyNode> symbols, SymbolKind kind, int maxWidth)
    {
        var currentWidth = 0;
        var isFirstSymbol = true;
        
        foreach (var symbol in symbols)
        {
            var needsSpace = !isFirstSymbol;
            var symbolWidth = symbol.Name.Length + (needsSpace ? 1 : 0);
            
            // Check if we need to wrap
            if (currentWidth + symbolWidth > maxWidth && currentWidth > 0)
            {
                Console.WriteLine();
                Console.Write(new string(' ', 80 - maxWidth)); // Indent continuation
                currentWidth = 0;
                needsSpace = false;
            }
            
            if (needsSpace)
            {
                Console.Write(" ");
                currentWidth += 1;
            }
            
            // Use proper color helper method
            WriteColoredSymbol(symbol.Name, kind);
            
            currentWidth += symbol.Name.Length;
            isFirstSymbol = false;
        }
    }

    private void WriteColoredSymbol(string symbolName, SymbolKind kind)
    {
        var semanticType = kind switch
        {
            SymbolKind.Function => SemanticColorType.Function,
            SymbolKind.Method => SemanticColorType.Function,
            SymbolKind.Class => SemanticColorType.Class,
            SymbolKind.Interface => SemanticColorType.Interface,
            SymbolKind.Module => SemanticColorType.Module,
            SymbolKind.Namespace => SemanticColorType.Namespace,
            _ => SemanticColorType.Function
        };
        
        var (r, g, b) = _colorEngine.GenerateSemanticColor(symbolName, semanticType);
        Console.Write($"\u001b[48;2;{r};{g};{b}m\u001b[30m{symbolName}\u001b[0m");
    }


    private string GetKindDisplayName(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Function => "functions",
            SymbolKind.Method => "methods",
            SymbolKind.Class => "classes",
            SymbolKind.Interface => "interfaces",
            SymbolKind.Module => "modules",
            SymbolKind.Namespace => "namespaces",
            _ => kind.ToString().ToLower()
        };
    }

    private string GetBackgroundColor(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Function => "\u001b[48;2;70;130;180m",   // Steel Blue
            SymbolKind.Method => "\u001b[48;2;100;149;237m",    // Cornflower Blue  
            SymbolKind.Class => "\u001b[48;2;135;206;235m",     // Sky Blue
            SymbolKind.Interface => "\u001b[48;2;173;216;230m", // Light Blue
            SymbolKind.Module => "\u001b[48;2;176;196;222m",    // Light Steel Blue
            SymbolKind.Namespace => "\u001b[48;2;191;239;255m", // Alice Blue
            _ => "\u001b[48;2;220;220;220m" // Light Gray
        };
    }

    private string GetSymbolIcon(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Function => "ƒ",
            SymbolKind.Method => "ƒ",
            SymbolKind.Class => "C",
            SymbolKind.Interface => "I",
            SymbolKind.Module => "📁",
            SymbolKind.Namespace => "N",
            SymbolKind.Property => "P",
            SymbolKind.Field => "F",
            SymbolKind.Variable => "V",
            SymbolKind.Parameter => "p",
            _ => "?"
        };
    }

    private void ShowHelp()
    {
        Console.WriteLine("Thaum - LSP-based codebase summarization tool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  thaum <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  ls [path]              List symbols in hierarchical format");
        Console.WriteLine("  summarize [path]       Generate codebase summaries");
        Console.WriteLine("  help                   Show this help message");
        Console.WriteLine();
        Console.WriteLine("Options for 'ls':");
        Console.WriteLine("  --path <path>          Project path (default: current directory)");
        Console.WriteLine("  --lang <language>      Language (python, csharp, javascript, etc.)");
        Console.WriteLine("  --depth <number>       Maximum nesting depth (default: 10)");
        Console.WriteLine("  --types                Show symbol types");
        Console.WriteLine();
        Console.WriteLine("Options for 'summarize':");
        Console.WriteLine("  --path <path>          Project path (default: current directory)");
        Console.WriteLine("  --lang <language>      Language (python, csharp, javascript, etc.)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  thaum ls");
        Console.WriteLine("  thaum ls /path/to/project --lang python --depth 3");
        Console.WriteLine("  thaum summarize --path ./src --lang csharp");
        Console.WriteLine();
        Console.WriteLine("Run without arguments to launch the interactive TUI.");
    }
}

internal record LsOptions(string ProjectPath, string Language, int MaxDepth, bool ShowTypes);
internal record SummarizeOptions(string ProjectPath, string Language);

internal class HierarchyNode
{
    public string Name { get; }
    public SymbolKind Kind { get; }
    public CodeSymbol? Symbol { get; }
    public List<HierarchyNode> Children { get; } = new();

    public HierarchyNode(string name, SymbolKind kind, CodeSymbol? symbol)
    {
        Name = name;
        Kind = kind;
        Symbol = symbol;
    }
}