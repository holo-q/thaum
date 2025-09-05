using Thaum.Core.Models;
using Thaum.Core.Utils;

namespace Thaum.CLI.Models;

public class TreeNode {
	public string              Name     { get; }
	public SymbolKind          Kind     { get; }
	public CodeSymbol?         Symbol   { get; }
	public List<TreeNode> Children { get; } = new();

	private readonly PerceptualColorer _colorer;

	public TreeNode(string name, SymbolKind kind, CodeSymbol? symbol, PerceptualColorer? colorEngine = null) {
		Name   = name;
		Kind   = kind;
		Symbol = symbol;
		_colorer = colorEngine ?? new PerceptualColorer();
	}

	public static List<TreeNode> BuildHierarchy(List<CodeSymbol> symbols, PerceptualColorer colorer) {
		List<TreeNode>                        nodes         = new List<TreeNode>();
		IEnumerable<IGrouping<string, CodeSymbol>> symbolsByFile = symbols.GroupBy(s => s.FilePath);

		foreach (IGrouping<string, CodeSymbol> fileGroup in symbolsByFile) {
			string relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), fileGroup.Key);
			TreeNode fileNode     = new TreeNode(relativePath, SymbolKind.Module, null, colorer);

			// Include all meaningful symbol types (for both code and assembly inspection)
			List<CodeSymbol> filteredSymbols = fileGroup.Where(s =>
				s.Kind == SymbolKind.Class ||
				s.Kind == SymbolKind.Interface ||
				s.Kind == SymbolKind.Enum ||
				s.Kind == SymbolKind.EnumMember ||
				s.Kind == SymbolKind.Namespace ||
				s.Kind == SymbolKind.Function ||
				s.Kind == SymbolKind.Method ||
				s.Kind == SymbolKind.Constructor ||
				s.Kind == SymbolKind.Property ||
				s.Kind == SymbolKind.Field).ToList();

			if (filteredSymbols.Any()) {
				foreach (CodeSymbol symbol in filteredSymbols.OrderBy(s => s.StartPosition.Line)) {
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
			c.Kind == SymbolKind.Class ||
			c.Kind == SymbolKind.Interface ||
			c.Kind == SymbolKind.Function ||
			c.Kind == SymbolKind.Method ||
			c.Kind == SymbolKind.Property ||
			c.Kind == SymbolKind.Field).ToList();

		foreach (CodeSymbol child in filteredChildren.OrderBy(c => c.StartPosition.Line)) {
			TreeNode childNode = new TreeNode(child.Name, child.Kind, child, colorer);
			parent.Children.Add(childNode);

			if (child.Children?.Any() == true) {
				AddChildSymbols(childNode, child.Children, colorer);
			}
		}
	}

	public static void DisplayHierarchy(List<TreeNode> nodes, LsOptions options) {
		foreach (TreeNode node in nodes) {
			node.DisplayNodeGrouped("", true, options, 0);
		}
	}

	private void DisplayNodeGrouped(string prefix, bool isLast, LsOptions options, int depth) {
		if (depth >= options.MaxDepth) return;

		string connector = isLast ? "└── " : "├── ";
		string symbol    = GetSymbolIcon(Kind);

		Console.WriteLine($"{prefix}{connector}{symbol} {Name}");

		if (Children.Any()) {
			string newPrefix = prefix + (isLast ? "    " : "│   ");

			// Group children by type
			List<IGrouping<SymbolKind, TreeNode>> groupedChildren = Children.GroupBy(c => c.Kind).ToList();

			foreach (IGrouping<SymbolKind, TreeNode> group in groupedChildren) {
				bool isLastGroup = group == groupedChildren.Last();
				DisplaySymbolGroup(group.Key, group.ToList(), newPrefix, isLastGroup, options, depth + 1);
			}
		}
	}

	private void DisplaySymbolGroup(SymbolKind kind, List<TreeNode> symbols, string prefix, bool isLast, LsOptions options, int depth) {
		if (depth >= options.MaxDepth || !symbols.Any()) return;

		string connector = isLast ? "└── " : "├── ";
		string icon      = GetSymbolIcon(kind);
		string kindName  = GetKindDisplayName(kind);

		// Print prefix and label normally
		string linePrefix = $"{prefix}{connector}{icon} {kindName}: ";
		Console.Write(linePrefix);

		// Print symbols with Terminal.Gui colors
		PrintColoredSymbols(symbols, kind, 80 - linePrefix.Length, options.NoColors);

		Console.WriteLine(); // End the line
	}

	private void PrintColoredSymbols(List<TreeNode> symbols, SymbolKind kind, int maxWidth, bool noColors = false) {
		int currentWidth  = 0;
		bool isFirstSymbol = true;

		foreach (TreeNode symbol in symbols) {
			bool needsSpace  = !isFirstSymbol;
			int symbolWidth = symbol.Name.Length + (needsSpace ? 1 : 0);

			// Check if we need to wrap
			if (currentWidth + symbolWidth > maxWidth && currentWidth > 0) {
				Console.WriteLine();
				Console.Write(new string(' ', 80 - maxWidth)); // Indent continuation
				currentWidth = 0;
				needsSpace   = false;
			}

			if (needsSpace) {
				Console.Write(" ");
				currentWidth += 1;
			}

			// Use proper color helper method
			WriteColoredSymbol(symbol.Name, kind, noColors);

			currentWidth  += symbol.Name.Length;
			isFirstSymbol =  false;
		}
	}

	private void WriteColoredSymbol(string symbolName, SymbolKind kind, bool noColors = false) {
		if (noColors) {
			Console.Write(symbolName);
			return;
		}

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
		Console.Write($"\u001b[48;2;{r};{g};{b}m\u001b[38;2;0;0;0m{symbolName}\u001b[0m");
	}

	private static string GetSymbolIcon(SymbolKind kind) {
		return kind switch {
			SymbolKind.Function  => "ƒ",
			SymbolKind.Method    => "ƒ",
			SymbolKind.Class     => "C",
			SymbolKind.Interface => "I",
			SymbolKind.Module    => "📁",
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