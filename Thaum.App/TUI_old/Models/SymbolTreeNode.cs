using Terminal.Gui.Views;
using Thaum.Core.Models;

namespace Thaum.TUI.Models;

/// <summary>
/// Tree node implementation for displaying code symbols in Terminal.Gui v2 TreeView
/// where each node represents either a file or a symbol within that file
/// following the proper ITreeNode pattern for native TreeView integration
/// </summary>
public class SymbolTreeNode : ITreeNode
{
	private readonly CodeSymbol?          _symbol;
	private readonly string               _filePath;
	private readonly List<SymbolTreeNode> _children = [];

	public SymbolTreeNode(string filePath)
	{
		_filePath = filePath;
		_symbol   = null;
	}

	public SymbolTreeNode(CodeSymbol symbol, string filePath)
	{
		_symbol   = symbol;
		_filePath = filePath;
	}

	public CodeSymbol? Symbol   => _symbol;
	public string      FilePath => _filePath;
	public bool        IsFile   => _symbol == null;

	public string Text
	{
		get => IsFile
			? $"📁 {Path.GetFileName(_filePath)}"
			: $"{GetSymbolIcon(_symbol!.Kind)} {_symbol!.Name}";
		set { } // Not used for our implementation
	}

	public object Tag { get; set; } = null!;

	public IList<ITreeNode> Children => _children.Cast<ITreeNode>().ToList();

	public void AddChild(SymbolTreeNode child)
	{
		_children.Add(child);
	}

	private static string GetSymbolIcon(SymbolKind kind) => kind switch
	{
		SymbolKind.Class       => "🏛️",
		SymbolKind.Interface   => "🔌",
		SymbolKind.Method      => "⚙️",
		SymbolKind.Function    => "🔧",
		SymbolKind.Property    => "📝",
		SymbolKind.Field       => "📦",
		SymbolKind.Variable    => "📊",
		SymbolKind.Enum        => "📋",
		SymbolKind.EnumMember  => "📄",
		SymbolKind.Constructor => "🏗️",
		SymbolKind.Namespace   => "📂",
		_                      => "❓"
	};

	public static List<SymbolTreeNode> BuildFromCodeMap(CodeMap codeMap)
	{
		var fileNodes = new List<SymbolTreeNode>();

		foreach (var symbol in codeMap)
		{
			var fileNode = fileNodes.FirstOrDefault(f => f.FilePath == symbol.FilePath);
			if (fileNode == null)
			{
				fileNode = new SymbolTreeNode(symbol.FilePath);
				fileNodes.Add(fileNode);
			}

			fileNode.AddChild(new SymbolTreeNode(symbol, symbol.FilePath));
		}

		return fileNodes.OrderBy(f => Path.GetFileName(f.FilePath)).ToList();
	}
}