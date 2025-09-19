using System.Text.Json.Serialization;

namespace Thaum.Core.Crawling;

public record CodeSymbol(
	string            Name,
	SymbolKind        Kind,
	string            FilePath,
	CodeLoc           StartCodeLoc,
	CodeLoc           EndCodeLoc,
	string?           Summary      = null,
	string?           ExtractedKey = null,
	List<CodeSymbol>? Children     = null,
	List<string>?     Dependencies = null,
	DateTime?         LastModified = null
) {
	[JsonIgnore]
	public bool IsSummarized => !string.IsNullOrEmpty(Summary);

	[JsonIgnore]
	public bool HasExtractedKey => !string.IsNullOrEmpty(ExtractedKey);
}

public enum SymbolKind {
	Function,
	Method,
	Constructor,
	Class,
	Interface,
	Enum,
	EnumMember,
	Module,
	Namespace,
	Property,
	Field,
	Variable,
	Parameter
}

public record CodeLoc(int Line, int Character);

public record SymbolHierarchy(
	string                     ProjectPath,
	List<CodeSymbol>           RootSymbols,
	Dictionary<string, string> ExtractedKeys,
	DateTime                   LastUpdated
);