using System.Collections;

namespace Thaum.Core.Crawling;

/// <summary>
/// Encapsulates the results of code crawling operations where symbols are organized by file
/// where statistics track discovery progress where the map provides structured access to
/// discovered symbols while maintaining efficient lookups and iteration capabilities
/// </summary>
public class CodeMap : IEnumerable<CodeSymbol> {
	private readonly Dictionary<string, List<CodeSymbol>> _symbolsByFile = new();
	private readonly List<CodeSymbol>                     _allSymbols    = new();
	private readonly Dictionary<string, CodeSymbol>       _symbolsByName = new();

	/// <summary>
	/// All discovered symbols in discovery order where the list maintains the complete
	/// set of symbols found during crawling operations
	/// </summary>
	public IReadOnlyList<CodeSymbol> Symbols => _allSymbols.AsReadOnly();

	/// <summary>
	/// Total count of discovered symbols where count reflects all symbols added to the map
	/// </summary>
	public int Count => _allSymbols.Count;

	/// <summary>
	/// All file paths that contain discovered symbols where paths provide coverage information
	/// </summary>
	public IReadOnlyCollection<string> Files => _symbolsByFile.Keys;

	/// <summary>
	/// Number of files containing symbols where file count indicates breadth of discovery
	/// </summary>
	public int FileCount => _symbolsByFile.Count;

	/// <summary>
	/// Adds symbol to the map where the symbol is indexed by file and name for efficient access
	/// where duplicate symbols by name are handled by keeping the most recent addition
	/// </summary>
	public CodeMap AddSymbol(CodeSymbol symbol) {
		_allSymbols.Add(symbol);

		// Index by file path
		if (!_symbolsByFile.ContainsKey(symbol.FilePath)) {
			_symbolsByFile[symbol.FilePath] = new List<CodeSymbol>();
		}
		_symbolsByFile[symbol.FilePath].Add(symbol);

		// Index by name (latest wins for duplicates)
		_symbolsByName[symbol.Name] = symbol;

		return this;
	}

	/// <summary>
	/// Adds multiple symbols to the map where each symbol is processed through AddSymbol
	/// </summary>
	public CodeMap AddSymbols(IEnumerable<CodeSymbol> symbols) {
		foreach (CodeSymbol symbol in symbols) {
			AddSymbol(symbol);
		}
		return this;
	}

	/// <summary>
	/// Gets all symbols in the specified file where empty collection is returned for unknown files
	/// </summary>
	public IReadOnlyList<CodeSymbol> GetSymbolsInFile(string filePath) {
		return _symbolsByFile.TryGetValue(filePath, out List<CodeSymbol>? symbols)
			? symbols.AsReadOnly()
			: Array.Empty<CodeSymbol>();
	}

	/// <summary>
	/// Gets symbol by name where null is returned if no symbol with that name exists
	/// where the most recently added symbol wins in case of name conflicts
	/// </summary>
	public CodeSymbol? GetSymbolByName(string name) {
		return _symbolsByName.TryGetValue(name, out CodeSymbol? symbol) ? symbol : null;
	}

	/// <summary>
	/// Filters symbols by kind where the filter preserves discovery order
	/// </summary>
	public IEnumerable<CodeSymbol> GetSymbolsByKind(SymbolKind kind) {
		return _allSymbols.Where(s => s.Kind == kind);
	}

	/// <summary>
	/// Checks if any symbols exist in the specified file
	/// </summary>
	public bool HasSymbolsInFile(string filePath) {
		return _symbolsByFile.ContainsKey(filePath) && _symbolsByFile[filePath].Count > 0;
	}

	/// <summary>
	/// Checks if a symbol with the given name exists in the map
	/// </summary>
	public bool HasSymbol(string name) {
		return _symbolsByName.ContainsKey(name);
	}

	/// <summary>
	/// Clears all symbols from the map resetting it to empty state
	/// </summary>
	public CodeMap Clear() {
		_allSymbols.Clear();
		_symbolsByFile.Clear();
		_symbolsByName.Clear();
		return this;
	}

	/// <summary>
	/// Creates a new empty CodeMap ready for symbol addition
	/// </summary>
	public static CodeMap Create() => new CodeMap();

	/// <summary>
	/// Creates a CodeMap pre-populated with the given symbols
	/// </summary>
	public static CodeMap FromSymbols(IEnumerable<CodeSymbol> symbols) {
		CodeMap map = new CodeMap();
		map.AddSymbols(symbols);
		return map;
	}

	/// <summary>
	/// Converts the CodeMap back to a simple list of symbols in discovery order
	/// </summary>
	public List<CodeSymbol> ToList() => new List<CodeSymbol>(_allSymbols);

	/// <summary>
	/// Enables foreach iteration over all symbols in discovery order
	/// </summary>
	public IEnumerator<CodeSymbol> GetEnumerator() => _allSymbols.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>
	/// Provides debugging information about the map contents
	/// </summary>
	public override string ToString() =>
		$"CodeMap: {Count} symbols across {FileCount} files";
}