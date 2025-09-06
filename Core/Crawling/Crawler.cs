using Thaum.Core.Models;

namespace Thaum.Core.Services;

/// <summary>
/// Stateless crawler for code symbols.
/// </summary>
/// <summary>
/// Abstract base for code crawling implementations where crawling means parsing/analyzing/extracting
/// symbols from source code where different crawlers (TreeSitter/LSP/Roslyn) share this interface
/// where the abstraction enables swappable parsing backends while maintaining consistent API
/// </summary>
public abstract class Crawler {
	public abstract Task<List<CodeSymbol>> CrawlDir(string         directory);
	public abstract Task<List<CodeSymbol>> CrawlFile(string        filePath);
	public abstract Task<CodeSymbol?>      GetDefinitionFor(string name, CodeLoc location);
	public abstract Task<List<CodeSymbol>> GetReferencesFor(string name, CodeLoc location);

	/// <summary>
	/// Extracts source code for given symbol where retrieval includes proper bounds checking
	/// where the method handles different symbol types appropriately extracting complete definitions
	/// </summary>
	public abstract Task<string?> GetCode(CodeSymbol symbol);
}