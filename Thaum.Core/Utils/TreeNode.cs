using Spectre.Console;
using Thaum.Core.Crawling;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.Core.Utils;

public class TreeNode {
	public string         Name     { get; }
	public SymbolKind     Kind     { get; }
	public CodeSymbol?    Symbol   { get; }
	public List<TreeNode> Children { get; } = new();

	private readonly PerceptualColorer _colorer;

	public TreeNode(string name, SymbolKind kind, CodeSymbol? symbol, PerceptualColorer? colorEngine = null) {
		Name     = name;
		Kind     = kind;
		Symbol   = symbol;
		_colorer = colorEngine ?? new PerceptualColorer();
	}

	public static List<TreeNode> BuildHierarchy(List<CodeSymbol> symbols, PerceptualColorer colorer) {
		List<TreeNode>                             nodes         = new List<TreeNode>();
		IEnumerable<IGrouping<string, CodeSymbol>> symbolsByFile = symbols.GroupBy(s => s.FilePath);

		foreach (IGrouping<string, CodeSymbol> fileGroup in symbolsByFile) {
			string   relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fileGroup.Key);
			TreeNode fileNode     = new TreeNode(relativePath, SymbolKind.Module, null, colorer);

			// Include all meaningful symbol types (for both code and assembly inspection)
			List<CodeSymbol> filteredSymbols = fileGroup.Where(s =>
				s.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Enum or SymbolKind.EnumMember or SymbolKind.Namespace or SymbolKind.Function or SymbolKind.Method or SymbolKind.Constructor or SymbolKind.Property or SymbolKind.Field).ToList();

			if (filteredSymbols.Any()) {
				foreach (CodeSymbol symbol in filteredSymbols.OrderBy(s => s.StartCodeLoc.Line)) {
					TreeNode symbolNode = new TreeNode(symbol.Name, symbol.Kind, symbol, colorer);
					fileNode.Children.Add(symbolNode);

					// Add nested symbols if any (only classes and functions)
					if (symbol.Children?.Any() == true) {
						AddChildSymbols(symbolNode, symbol.Children, colorer);
					}
				}

				nodes.Add(fileNode);
			}
		}

		return nodes.OrderBy(n => n.Name).ToList();
	}

	private static void AddChildSymbols(TreeNode parent, List<CodeSymbol> children, PerceptualColorer colorer) {
		// Include all meaningful symbol types for assembly inspection
		List<CodeSymbol> filteredChildren = children.Where(c =>
			c.Kind is SymbolKind.Class or SymbolKind.Interface or SymbolKind.Function or SymbolKind.Method or SymbolKind.Property or SymbolKind.Field).ToList();

		foreach (CodeSymbol child in filteredChildren.OrderBy(c => c.StartCodeLoc.Line)) {
			TreeNode childNode = new TreeNode(child.Name, child.Kind, child, colorer);
			parent.Children.Add(childNode);

			if (child.Children?.Any() == true) {
				AddChildSymbols(childNode, child.Children, colorer);
			}
		}
	}

	public static void DisplayHierarchy(List<TreeNode> nodes, int maxDepth) {
		foreach (TreeNode node in nodes) {
			node.DisplayNodeGrouped("", true, maxDepth, 0);
		}
	}

	private void DisplayNodeGrouped(string prefix, bool isLast, int maxDepth, int depth) {
		if (depth >= maxDepth) return;

		string connector = isLast ? "└── " : "├── ";
		string symbol    = GetSymbolIcon(Kind);

		println($"{prefix}{connector}{symbol} {Name}");

		if (Children.Any()) {
			string newPrefix = prefix + (isLast ? "    " : "│   ");

			// Group children by type
			List<IGrouping<SymbolKind, TreeNode>> groupedChildren = Children.GroupBy(c => c.Kind).ToList();

			foreach (IGrouping<SymbolKind, TreeNode> group in groupedChildren) {
				bool isLastGroup = group == groupedChildren.Last();
				DisplaySymbolGroup(group.Key, group.ToList(), newPrefix, isLastGroup, maxDepth, depth + 1);
			}
		}
	}

    private void DisplaySymbolGroup(SymbolKind kind, List<TreeNode> symbols, string prefix, bool isLast, int maxDepth, int depth, bool noColors = false) {
        if (depth >= maxDepth || !symbols.Any()) return;

        string connector = isLast ? "└── " : "├── ";
        string icon      = GetSymbolIcon(kind);
        string kindName  = GetKindDisplayName(kind);

        // Compose the left prefix "├── X label: " and compute console width
        string linePrefix = $"{prefix}{connector}{icon} {kindName}: ";
        int totalWidth = GetConsoleWidth();
        int available  = Math.Max(20, totalWidth - linePrefix.Length);

        // Build wrapped lines with Spectre markup tokens and render via Spectre directly
        List<string> tokens = symbols.Select(s => BuildToken(s.Name, kind, noColors)).ToList();
        foreach (string line in BuildWrappedMarkupLines(tokens, symbols.Select(s => s.Name.Length).ToList(), available, linePrefix, new string(' ', linePrefix.Length))) {
            // Use Spectre to render markup so colors work regardless of Serilog sink
            AnsiConsole.MarkupLine(line);
        }
    }

    private static int GetConsoleWidth() {
        try { return Console.WindowWidth; } catch { return GLB.ConsoleMinWidth; }
    }

    private static IEnumerable<string> BuildWrappedMarkupLines(List<string> markupTokens, List<int> plainTokenLengths, int maxWidth, string firstPrefix, string contPrefix) {
        List<string> lines   = new List<string>();
        string current = firstPrefix;
        int    width   = 0;
        for (int i = 0; i < markupTokens.Count; i++) {
            string token = markupTokens[i];
            int tokenLen = plainTokenLengths[i];
            int needed = (width == 0 ? 0 : 1) + tokenLen; // space + visible chars
            if (width + needed > maxWidth && width > 0) {
                lines.Add(current);
                current = contPrefix + markupTokens[i];
                width = tokenLen;
            } else {
                if (width > 0) { current += " "; }
                current += token;
                width += needed;
            }
        }
        if (!string.IsNullOrEmpty(current)) lines.Add(current);
        return lines;
    }

    private string BuildToken(string symbolName, SymbolKind kind, bool noColors) {
        string safe = Markup.Escape(symbolName);
        if (noColors) return safe;

        SemanticColorType semanticType = kind switch {
            SymbolKind.Function  => SemanticColorType.Function,
            SymbolKind.Method    => SemanticColorType.Function,
            SymbolKind.Class     => SemanticColorType.Class,
            SymbolKind.Interface => SemanticColorType.Interface,
            SymbolKind.Module    => SemanticColorType.Module,
            SymbolKind.Namespace => SemanticColorType.Namespace,
            _                    => SemanticColorType.Function
        };

        (int r, int g, int b) = _colorer.GenerateSemanticColor(symbolName, semanticType);
        // Foreground black on semantic background for clear label chips
        return $"[black on rgb({r},{g},{b})]{safe}[/]";
    }

    private static string GetSymbolIcon(SymbolKind kind) {
        return kind switch {
            SymbolKind.Function  => "ƒ",
            SymbolKind.Method    => "ƒ",
            SymbolKind.Class     => "C",
            SymbolKind.Interface => "I",
            SymbolKind.Module    => "DIR",
            SymbolKind.Namespace => "N",
            SymbolKind.Property  => "P",
            SymbolKind.Field     => "F",
            SymbolKind.Variable  => "V",
            SymbolKind.Parameter => "p",
			_                    => "?"
		};
	}

	private static string GetKindDisplayName(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "functions",
			SymbolKind.Method    => "methods",
			SymbolKind.Class     => "classes",
			SymbolKind.Interface => "interfaces",
			SymbolKind.Module    => "modules",
			SymbolKind.Namespace => "namespaces",
			_                    => kind.ToString().ToLower()
		};
	}
}
