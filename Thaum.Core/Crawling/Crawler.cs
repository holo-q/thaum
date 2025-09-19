namespace Thaum.Core.Crawling;

/// <summary>
/// Abstract base for code crawling implementations where crawling means parsing/analyzing/extracting
/// symbols from source code where different crawlers (TreeSitter/LSP/Roslyn) share this interface
/// where the abstraction enables swappable parsing backends while maintaining consistent API
/// where CodeMap parameter gets updated during crawling and returned for structured access
/// </summary>
public abstract class Crawler {
	/// <summary>
	/// Crawls directory for symbols updating the provided CodeMap where the map accumulates
	/// all discovered symbols organized by file and indexed by name for efficient access
	/// </summary>
	public abstract Task<CodeMap> CrawlDir(string directory, CodeMap? codeMap = null);
	
	/// <summary>
	/// Crawls single file for symbols updating the provided CodeMap where file-specific
	/// symbols are added to the map maintaining discovery order and indexing
	/// </summary>
	public abstract Task<CodeMap> CrawlFile(string filePath, CodeMap? codeMap = null);
	
	/// <summary>
	/// Legacy method for backward compatibility - creates new CodeMap and returns symbols as list
	/// </summary>
	public async Task<List<CodeSymbol>> CrawlDirLegacy(string directory) {
		CodeMap codeMap = await CrawlDir(directory);
		return codeMap.ToList();
	}
	
	/// <summary>
	/// Legacy method for backward compatibility - creates new CodeMap and returns symbols as list
	/// </summary>
	public async Task<List<CodeSymbol>> CrawlFileLegacy(string filePath) {
		CodeMap codeMap = await CrawlFile(filePath);
		return codeMap.ToList();
	}
	
	public abstract Task<CodeSymbol?>      GetDefinitionFor(string name, CodeLoc location);
	public abstract Task<List<CodeSymbol>> GetReferencesFor(string name, CodeLoc location);

	/// <summary>
	/// Extracts source code for given symbol where retrieval includes proper bounds checking
	/// where the method handles different symbol types appropriately extracting complete definitions
	/// </summary>
	public abstract Task<string?> GetCode(CodeSymbol symbol);
}